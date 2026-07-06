using System;
using System.Collections;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
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
    [SerializeField] private TMP_Text artworkTitleText;

    [SerializeField] private Button descriptionOpenButton;
    [SerializeField] private Button descriptionCloseButton;

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
    [SerializeField] private float keyboardHideGraceSeconds = 0.35f;

    [SerializeField] private RawImage frozenCameraImage;

    private Texture2D frozenFrameTexture;
    private bool isOpeningChat;

    private const string apiurl = "https://api.scripictural.tecshield.net/api/artworks/ask-ai";
    private const string UnpublishedArtworkMessage = "Artwork not published";
    private const string ScanArtworkFirstMessage = "Scan any artwork first";

    private string artworkID = string.Empty;
    private string cachedArtworkTitle = string.Empty;
    private bool isCurrentArtworkPublished = true;
    private bool isWaitingResponse;

    private Coroutine dotsCoroutine;
    private Coroutine scrollCoroutine;
    private Coroutine requestCoroutine;

    private RectTransform inputRect;
    private RectTransform submitRect;
    private Vector2 originalScrollOffsetMin;
    private Vector2 originalInputAnchoredPos;
    private Vector2 originalSubmitAnchoredPos;
    private float currentKeyboardOffset;
    private float lastAppliedKeyboardOffset = -1f;
    private float cachedKeyboardHeight;
    private float keyboardVisibleUntil;

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

        WireSubmitButton();
        ConfigureInputFieldCaret();
        openChatBotButton.onClick.AddListener(OpenChatBot);
        closeChatBotButton.onClick.AddListener(CloseChatBot);

        closeChatBotButton.gameObject.SetActive(false);

        EnsureArtworkTitleText();
        RefreshArtworkTitleDisplay();
        ApplyChatInputState();
    }

    private void Update()
    {
        HandleMobileKeyboard();
    }

    private void OnDestroy()
    {
        UnwireSubmitButton();
        openChatBotButton.onClick.RemoveListener(OpenChatBot);
        closeChatBotButton.onClick.RemoveListener(CloseChatBot);
        SetCameraPaused(false);
        ClearFrozenFrame();
    }

    private void OnEnable()
    {
        ClearMessages();
    }

    public void SetCurrentArtworkId(string id)
    {
        SetCurrentArtworkInfo(id, cachedArtworkTitle, isCurrentArtworkPublished);
    }

    public void SetCurrentArtworkInfo(string id, string title, bool isPublished)
    {
        bool artworkChanged = false;

        if (!string.IsNullOrWhiteSpace(id))
        {
            artworkChanged = !string.Equals(artworkID, id, StringComparison.Ordinal);
            artworkID = id;
            Debug.Log("Artwork id set for chatbot: " + artworkID);
        }

        cachedArtworkTitle = title ?? string.Empty;
        isCurrentArtworkPublished = isPublished;

        // A different artwork was scanned: the old conversation no longer
        // applies, so reset the chat for the new artwork.
        if (artworkChanged && IsChatOpen)
        {
            StopDots();

            if (requestCoroutine != null)
            {
                StopCoroutine(requestCoroutine);
                requestCoroutine = null;
            }

            isWaitingResponse = false;
            ClearMessages();
        }

        RefreshArtworkTitleDisplay();
        ApplyChatInputState();
    }

    private void OpenChatBot()
    {
        if (isOpeningChat || IsChatOpen)
            return;

        StartCoroutine(OpenChatBotRoutine());
    }

    private IEnumerator OpenChatBotRoutine()
    {
        isOpeningChat = true;

        openChatBotButton.gameObject.SetActive(false);
        closeChatBotButton.gameObject.SetActive(false);

        descriptionCloseButton.gameObject.SetActive(false);
        descriptionOpenButton.gameObject.SetActive(false);

        yield return new WaitForEndOfFrame();

        CaptureFrozenFrame();
        SetCameraPaused(true);

        chatParent.SetActive(true);
        closeChatBotButton.gameObject.SetActive(true);

        ClearMessages();

        RefreshArtworkTitleDisplay();
        ApplyChatInputState();
        ScrollToBottom();

        if (isCurrentArtworkPublished && HasScannedArtwork())
            textInput.ActivateInputField();

        isOpeningChat = false;
    }

    private void CloseChatBot()
    {
        ResetKeyboardLayout();

        chatParent.SetActive(false);
        openChatBotButton.gameObject.SetActive(true);
        closeChatBotButton.gameObject.SetActive(false);

        SetCameraPaused(false);
        ClearFrozenFrame();

        descriptionCloseButton.gameObject.SetActive(false);
        descriptionOpenButton.gameObject.SetActive(true);
    }
    private void CaptureFrozenFrame()
    {
        // ClearFrozenFrame();

        // frozenFrameTexture = ScreenCapture.CaptureScreenshotAsTexture();

        // if (frozenCameraImage == null)
        //     return;

        // frozenCameraImage.texture = frozenFrameTexture;
        // frozenCameraImage.gameObject.SetActive(true);
    }

    private void ClearFrozenFrame()
    {
        // if (frozenCameraImage != null)
        // {
        //     frozenCameraImage.texture = null;
        //     frozenCameraImage.gameObject.SetActive(false);
        // }

        // if (frozenFrameTexture != null)
        // {
        //     Destroy(frozenFrameTexture);
        //     frozenFrameTexture = null;
        // }
    }
    private void OnSubmitClicked()
    {
        if (!HasScannedArtwork() || !isCurrentArtworkPublished)
            return;

        KeepInputFocused();

        if (isWaitingResponse)
            return;

        string message = textInput.text.Trim();

        if (string.IsNullOrEmpty(message))
            return;

        AddMyMessage(message);

        textInput.text = "";
        KeepInputFocused();

        AskAiBody body = new AskAiBody
        {
            artworkId = artworkID,
            question = message
        };

        requestCoroutine = StartCoroutine(SendApiAiRequest(body));
    }

    private IEnumerator SendApiAiRequest(AskAiBody aiBody)
    {
        isWaitingResponse = true;
        ApplyChatInputState();

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
        requestCoroutine = null;
        ApplyChatInputState();
        KeepInputFocused();
        ScrollToBottom();
    }

    private void ConfigureInputFieldCaret()
    {
        if (textInput == null)
            return;

        textInput.text = string.Empty;
        textInput.customCaretColor = true;
        textInput.caretColor = Color.black;
        textInput.caretWidth = 2;
        textInput.caretBlinkRate = 0.85f;
        // Required for TMP to render the in-field caret on mobile. When
        // hide-mobile-input is off, the OS owns the caret and TMP draws none.
        textInput.shouldHideMobileInput = true;

        if (textInput.textComponent != null)
            textInput.textComponent.verticalAlignment = VerticalAlignmentOptions.Middle;

        if (textInput.placeholder is TMP_Text placeholderText)
            placeholderText.verticalAlignment = VerticalAlignmentOptions.Middle;
    }

    private void WireSubmitButton()
    {
        if (submitButton == null)
            return;

        Navigation nav = submitButton.navigation;
        nav.mode = Navigation.Mode.None;
        submitButton.navigation = nav;

        ChatSendPointerHandler pointerHandler = submitButton.GetComponent<ChatSendPointerHandler>();
        if (pointerHandler == null)
            pointerHandler = submitButton.gameObject.AddComponent<ChatSendPointerHandler>();
        pointerHandler.Bind(OnSubmitClicked);

        if (textInput != null)
            textInput.onSubmit.AddListener(OnInputSubmitted);
    }

    private void UnwireSubmitButton()
    {
        if (textInput != null)
            textInput.onSubmit.RemoveListener(OnInputSubmitted);

        if (submitButton == null)
            return;

        ChatSendPointerHandler pointerHandler = submitButton.GetComponent<ChatSendPointerHandler>();
        if (pointerHandler != null)
            pointerHandler.Bind(null);
    }

    private void OnInputSubmitted(string _)
    {
        OnSubmitClicked();
    }

    private void KeepInputFocused()
    {
        if (textInput == null || !isCurrentArtworkPublished || !HasScannedArtwork())
            return;

        textInput.ActivateInputField();
        textInput.MoveTextEnd(false);
    }

    private void EnsureArtworkTitleText()
    {
        if (artworkTitleText != null || chatParent == null)
            return;

        Transform existing = chatParent.transform.Find("ArtworkTitleText");
        if (existing != null)
        {
            artworkTitleText = existing.GetComponent<TMP_Text>();
            return;
        }

        GameObject titleGo = new GameObject("ArtworkTitleText", typeof(RectTransform));
        titleGo.transform.SetParent(chatParent.transform, false);

        RectTransform rect = titleGo.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.05f, 0.9f);
        rect.anchorMax = new Vector2(0.95f, 0.98f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        TextMeshProUGUI titleLabel = titleGo.AddComponent<TextMeshProUGUI>();
        titleLabel.fontSize = 24f;
        titleLabel.alignment = TextAlignmentOptions.Center;
        titleLabel.color = Color.white;
        titleLabel.text = string.Empty;
        artworkTitleText = titleLabel;
    }

    private void RefreshArtworkTitleDisplay()
    {
        if (artworkTitleText == null)
            return;

        if (!HasScannedArtwork())
        {
            artworkTitleText.text = ScanArtworkFirstMessage;
            return;
        }

        artworkTitleText.text = isCurrentArtworkPublished
            ? cachedArtworkTitle
            : UnpublishedArtworkMessage;
    }

    private bool HasScannedArtwork() => !string.IsNullOrWhiteSpace(artworkID);

    private void ApplyChatInputState()
    {
        bool canSend = HasScannedArtwork() && isCurrentArtworkPublished && !isWaitingResponse;
        bool canType = HasScannedArtwork() && isCurrentArtworkPublished;

        if (textInput != null)
        {
            textInput.interactable = canType;
            textInput.readOnly = !canType;

            if (!canType)
                textInput.text = string.Empty;
        }

        if (submitButton != null)
            submitButton.interactable = canSend;
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
        cachedKeyboardHeight = 0f;
        keyboardVisibleUntil = 0f;
        ApplyKeyboardLayout(0f);
    }

    private float GetKeyboardHeight()
    {
        if (textInput == null)
            return 0f;

        if (TouchScreenKeyboard.visible)
        {
            float keyboardHeight = TouchScreenKeyboard.area.height;

            if (keyboardHeight <= 0f)
                keyboardHeight = Screen.height * keyboardFallbackScreenPercent;

            float scaleFactor = rootCanvas != null ? rootCanvas.scaleFactor : 1f;
            cachedKeyboardHeight = keyboardHeight / scaleFactor + keyboardPadding;
            keyboardVisibleUntil = Time.unscaledTime + keyboardHideGraceSeconds;
            return cachedKeyboardHeight;
        }

        if (Time.unscaledTime < keyboardVisibleUntil)
            return cachedKeyboardHeight;

        if (textInput.isFocused && cachedKeyboardHeight > 0f)
            return cachedKeyboardHeight;

        return 0f;
    }

    private void SetCameraPaused(bool paused)
    {
        // if (VuforiaBehaviour.Instance == null)
        //     return;

        // VuforiaBehaviour.Instance.enabled = !paused;
    }
}

public class ChatSendPointerHandler : MonoBehaviour, IPointerDownHandler
{
    private Action onSubmit;

    public void Bind(Action callback)
    {
        onSubmit = callback;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        onSubmit?.Invoke();
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