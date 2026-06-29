using System;
using System.Collections;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using Vuforia;

public class ChatManager : MonoBehaviour
{
    public static ChatManager instance;

    [Header("UI")]
    [SerializeField] private GameObject chatParent;
    [SerializeField] private TMP_InputField textInput;
    [SerializeField] private Button submitButton;
    [SerializeField] private Transform content;
    [SerializeField] private ScrollRect scrollRect;
    [SerializeField] private Button openChatBotButton;
    [SerializeField] private Button closeChatBotButton;

    [Header("Message Prefabs")]
    [SerializeField] private MessageItemUi myMessageItem;
    [SerializeField] private MessageItemUi responseMessageItem;

    [Header("Typing")]
    [SerializeField] private float typewriterDelay = 0.015f;
    [SerializeField] private float dotsDelay = 0.35f;

    [Header("Keyboard Mobile")]
    [SerializeField] private Canvas rootCanvas;
    [SerializeField] private RectTransform scrollAreaRect;
    [SerializeField] private float keyboardPadding = 16f;
    [SerializeField] private float keyboardFallbackScreenPercent = 0.38f;
    [SerializeField] private float keyboardMoveSpeed = 14f;

    private const string apiurl = "https://api.scripictural.tecshield.net/api/artworks/ask-ai";

    private string artworkID = string.Empty;
    private string cachedDescription = string.Empty;
    private bool descriptionShown;
    private bool isWaitingResponse;

    private Coroutine dotsCoroutine;
    private Coroutine scrollCoroutine;

    private RectTransform inputRect;
    private RectTransform submitRect;
    private Vector2 originalScrollOffsetMin;
    private Vector2 originalInputAnchoredPos;
    private Vector2 originalSubmitAnchoredPos;
    private float currentKeyboardOffset;
    private float lastAppliedKeyboardOffset = -1f;

    public bool IsChatOpen => chatParent != null && chatParent.activeSelf;

    private void Awake()
    {
        instance = this;
    }

    private void Start()
    {
        if (rootCanvas == null)
            rootCanvas = GetComponentInParent<Canvas>();

        if (scrollAreaRect == null && scrollRect != null)
            scrollAreaRect = scrollRect.GetComponent<RectTransform>();

        inputRect = textInput != null ? textInput.GetComponent<RectTransform>() : null;
        submitRect = submitButton != null ? submitButton.GetComponent<RectTransform>() : null;

        if (scrollAreaRect != null)
            originalScrollOffsetMin = scrollAreaRect.offsetMin;

        if (inputRect != null)
            originalInputAnchoredPos = inputRect.anchoredPosition;

        if (submitRect != null)
            originalSubmitAnchoredPos = submitRect.anchoredPosition;

        submitButton.onClick.AddListener(OnSubmitClicked);
        openChatBotButton.onClick.AddListener(OpenChatBot);
        closeChatBotButton.onClick.AddListener(CloseChatBot);

        closeChatBotButton.gameObject.SetActive(false);

        //SetCurrentArtworkId("6a3912a0f8cafc946b54df95");
        EnsureDescriptionShown();
    }

    private void Update()
    {
        HandleMobileKeyboard();
    }

    private void OnDestroy()
    {
        submitButton.onClick.RemoveListener(OnSubmitClicked);
        openChatBotButton.onClick.RemoveListener(OpenChatBot);
        closeChatBotButton.onClick.RemoveListener(CloseChatBot);
        SetCameraPaused(false);
    }

    private void OnEnable()
    {
        ClearMessages();
        descriptionShown = false;
        EnsureDescriptionShown();
    }

    public void SetCurrentDescription(string description)
    {
        cachedDescription = description;

        if (isActiveAndEnabled)
            EnsureDescriptionShown();
    }

    public void SetCurrentArtworkId(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return;

        artworkID = id;
        Debug.Log("Artwork id set for chatbot: " + artworkID);
    }

    private void OpenChatBot()
    {
        chatParent.SetActive(true);
        openChatBotButton.gameObject.SetActive(false);
        closeChatBotButton.gameObject.SetActive(true);

        SetCameraPaused(true);
        EnsureDescriptionShown();
        ScrollToBottom();
        textInput.ActivateInputField();
    }

    private void CloseChatBot()
    {
        ResetKeyboardLayout();
        chatParent.SetActive(false);
        openChatBotButton.gameObject.SetActive(true);
        closeChatBotButton.gameObject.SetActive(false);
        SetCameraPaused(false);
    }

    private void OnSubmitClicked()
    {
        if (isWaitingResponse)
            return;

        string message = textInput.text.Trim();

        if (string.IsNullOrEmpty(message))
            return;

        AddMyMessage(message);

        textInput.text = "";
        textInput.ActivateInputField();

        AskAiBody body = new AskAiBody
        {
            artworkId = artworkID,
            question = message
        };

        StartCoroutine(SendApiAiRequest(body));
    }

    private IEnumerator SendApiAiRequest(AskAiBody aiBody)
    {
        isWaitingResponse = true;
        submitButton.interactable = false;

        MessageItemUi responseBubble = AddResponseBubble("...");
        dotsCoroutine = StartCoroutine(AnimateDots(responseBubble));

        string jsonBody = JsonUtility.ToJson(aiBody);

        using UnityWebRequest webRequest = new UnityWebRequest(apiurl, UnityWebRequest.kHttpVerbPOST);
        byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);

        webRequest.uploadHandler = new UploadHandlerRaw(bodyRaw);
        webRequest.downloadHandler = new DownloadHandlerBuffer();
        webRequest.SetRequestHeader("Content-Type", "application/json");
        webRequest.SetRequestHeader("Accept", "application/json");

        yield return webRequest.SendWebRequest();

        StopDots();

        if (webRequest.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"API Error: {webRequest.responseCode} - {webRequest.error}");
            Debug.LogError(webRequest.downloadHandler.text);

            yield return responseBubble.PlayTypewriter(
                "Something went wrong. Please try again.",
                GetMaxBubbleWidth(),
                false,
                typewriterDelay,
                ScrollToBottomImmediate
            );

            ResetWaitingState();
            yield break;
        }

        Debug.Log("Chat response: " + webRequest.downloadHandler.text);

        AiResponse response = JsonUtility.FromJson<AiResponse>(webRequest.downloadHandler.text);

        if (response?.data?.answer == null)
        {
            Debug.LogError("Invalid AI response format: " + webRequest.downloadHandler.text);

            yield return responseBubble.PlayTypewriter(
                "Invalid response received.",
                GetMaxBubbleWidth(),
                false,
                typewriterDelay,
                ScrollToBottomImmediate
            );

            ResetWaitingState();
            yield break;
        }

        yield return responseBubble.PlayTypewriter(
            response.data.answer,
            GetMaxBubbleWidth(),
            false,
            typewriterDelay,
            ScrollToBottomImmediate
        );

        ResetWaitingState();
    }

    private void ResetWaitingState()
    {
        isWaitingResponse = false;
        submitButton.interactable = true;
        textInput.ActivateInputField();
        ScrollToBottom();
    }

    private IEnumerator AnimateDots(MessageItemUi bubble)
    {
        int dotCount = 1;

        while (true)
        {
            bubble.SetMessage(new string('.', dotCount), GetMaxBubbleWidth(), false);
            ScrollToBottomImmediate();

            dotCount++;
            if (dotCount > 3)
                dotCount = 1;

            yield return new WaitForSeconds(dotsDelay);
        }
    }

    private void StopDots()
    {
        if (dotsCoroutine != null)
        {
            StopCoroutine(dotsCoroutine);
            dotsCoroutine = null;
        }
    }

    private void EnsureDescriptionShown()
    {
        if (descriptionShown)
            return;

        if (string.IsNullOrWhiteSpace(cachedDescription))
            return;

        if (content == null)
            return;

        AddResponseBubble(cachedDescription);
        descriptionShown = true;
        ScrollToBottom();
    }

    private void AddMyMessage(string message)
    {
        MessageItemUi msg = Instantiate(myMessageItem, content);
        msg.SetMessage(message, GetMaxBubbleWidth(), true);
        ScrollToBottom();
    }

    private MessageItemUi AddResponseBubble(string message)
    {
        MessageItemUi msg = Instantiate(responseMessageItem, content);
        msg.SetMessage(message, GetMaxBubbleWidth(), false);
        ScrollToBottom();
        return msg;
    }

    private float GetMaxBubbleWidth()
    {
        RectTransform contentRect = (RectTransform)content;
        float contentWidth = contentRect.rect.width;

        if (contentWidth <= 0f)
            contentWidth = Screen.width;

        return contentWidth * 0.8f;
    }

    private void ClearMessages()
    {
        if (content == null)
            return;

        foreach (Transform child in content)
            Destroy(child.gameObject);
    }

    private void ScrollToBottom()
    {
        if (scrollCoroutine != null)
            StopCoroutine(scrollCoroutine);

        scrollCoroutine = StartCoroutine(ScrollToBottomRoutine());
    }

    private IEnumerator ScrollToBottomRoutine()
    {
        yield return null;
        ScrollToBottomImmediate();
    }

    private void ScrollToBottomImmediate()
    {
        if (scrollRect == null)
            return;

        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)content);
        scrollRect.verticalNormalizedPosition = 0f;
    }

    private void HandleMobileKeyboard()
    {
        if (!IsChatOpen)
        {
            ResetKeyboardLayout();
            return;
        }

#if UNITY_ANDROID || UNITY_IOS
        float targetOffset = GetKeyboardHeight();
#else
        float targetOffset = 0f;
#endif

        currentKeyboardOffset = Mathf.Lerp(
            currentKeyboardOffset,
            targetOffset,
            Time.unscaledDeltaTime * keyboardMoveSpeed
        );

        ApplyKeyboardLayout(currentKeyboardOffset);

        if (Mathf.Abs(currentKeyboardOffset - lastAppliedKeyboardOffset) > 0.5f)
        {
            lastAppliedKeyboardOffset = currentKeyboardOffset;
            ScrollToBottom();
        }
    }

    private void ApplyKeyboardLayout(float offset)
    {
        if (scrollAreaRect != null)
            scrollAreaRect.offsetMin = originalScrollOffsetMin + new Vector2(0f, offset);

        if (inputRect != null)
            inputRect.anchoredPosition = originalInputAnchoredPos + new Vector2(0f, offset);

        if (submitRect != null)
            submitRect.anchoredPosition = originalSubmitAnchoredPos + new Vector2(0f, offset);
    }

    private void ResetKeyboardLayout()
    {
        currentKeyboardOffset = 0f;
        lastAppliedKeyboardOffset = -1f;
        ApplyKeyboardLayout(0f);
    }

    private float GetKeyboardHeight()
    {
        if (!textInput.isFocused || !TouchScreenKeyboard.visible)
            return 0f;

        float keyboardHeight = TouchScreenKeyboard.area.height;

        if (keyboardHeight <= 0f)
            keyboardHeight = Screen.height * keyboardFallbackScreenPercent;

        float scaleFactor = rootCanvas != null ? rootCanvas.scaleFactor : 1f;
        return keyboardHeight / scaleFactor + keyboardPadding;
    }

    private void SetCameraPaused(bool paused)
    {
        if (VuforiaBehaviour.Instance == null)
            return;

        VuforiaBehaviour.Instance.enabled = !paused;
    }
}

[Serializable]
public class AskAiBody
{
    public string artworkId;
    public string question;
}

[Serializable]
public class AiResponse
{
    public Answer data;
}

[Serializable]
public class Answer
{
    public string answer;
}