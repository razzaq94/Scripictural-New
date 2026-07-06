using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Vuforia;

public class DescriptionManager : MonoBehaviour
{
    public static DescriptionManager Instance;

    [SerializeField] private Button descriptionOpenButton;
    [SerializeField] private Button descriptionCloseButton;

    [SerializeField] private GameObject chatOpenButton;
    [SerializeField] private GameObject chatCloseButton;

    [SerializeField] private GameObject descriptionPanel;
    [SerializeField] private TextMeshProUGUI artworkTitle;
    [SerializeField] private TextMeshProUGUI artworkDescription;
    [SerializeField] private ScrollRect descriptionScrollRect;
    [SerializeField] private RectTransform descriptionContent;

    [SerializeField] private RawImage frozenCameraImage;
    [SerializeField] private float descriptionHorizontalPadding = 32f;

    private string cachedDescription = string.Empty;
    private string cachedTitle = string.Empty;
    private Texture2D frozenFrameTexture;
    private bool isOpening;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    private void Start()
    {
        descriptionOpenButton.onClick.RemoveAllListeners();
        descriptionCloseButton.onClick.RemoveAllListeners();

        descriptionOpenButton.onClick.AddListener(OpenDescriptionPanel);
        descriptionCloseButton.onClick.AddListener(CloseDescriptionPanel);

        descriptionCloseButton.gameObject.SetActive(false);

        if (frozenCameraImage != null)
            frozenCameraImage.gameObject.SetActive(false);

        ConfigureDescriptionText();
    }

    private void ConfigureDescriptionText()
    {
        if (artworkDescription == null)
            return;

        // Left-aligned body text reads better than centered for paragraphs.
        // Switch to HorizontalAlignmentOptions.Justified for a block look.
        artworkDescription.horizontalAlignment = HorizontalAlignmentOptions.Left;
        artworkDescription.verticalAlignment = VerticalAlignmentOptions.Middle;
        artworkDescription.lineSpacing = 12f;
        artworkDescription.enableWordWrapping = true;
        artworkDescription.overflowMode = TextOverflowModes.Overflow;
    }

    public void AddDescription(string msg)
    {
        cachedDescription = msg;
        RefreshOpenPanel();
    }

    public void AddTitle(string title)
    {
        cachedTitle = title;
        RefreshOpenPanel();
    }

    // If a new artwork is scanned while the panel is open, update it live.
    private void RefreshOpenPanel()
    {
        if (descriptionPanel == null || !descriptionPanel.activeSelf)
            return;

        if (string.IsNullOrEmpty(cachedTitle) || string.IsNullOrEmpty(cachedDescription))
            return;

        artworkTitle.text = cachedTitle;
        artworkDescription.text = cachedDescription;
        RefreshDescriptionLayout();
    }

    private void OpenDescriptionPanel()
    {
        if (isOpening)
            return;

        StartCoroutine(OpenDescriptionPanelRoutine());
    }

    private IEnumerator OpenDescriptionPanelRoutine()
    {
        isOpening = true;

        yield return new WaitForEndOfFrame();

        CaptureFrozenFrame();

        SetCameraPaused(true);

        if (!string.IsNullOrEmpty(cachedTitle) && !string.IsNullOrEmpty(cachedDescription))
        {
            artworkTitle.text = cachedTitle;
            artworkDescription.text = cachedDescription;
        }
        else
        {
            artworkTitle.text = "Scan an artwork first";
            artworkDescription.text = string.Empty;
        }

        chatOpenButton.SetActive(false);
        chatCloseButton.SetActive(false);

        descriptionPanel.SetActive(true);
        descriptionCloseButton.gameObject.SetActive(true);
        descriptionOpenButton.gameObject.SetActive(false);

        RefreshDescriptionLayout();

        isOpening = false;
    }

    private void RefreshDescriptionLayout()
    {
        if (descriptionContent == null || artworkDescription == null || descriptionScrollRect == null)
            return;

        RectTransform viewport = descriptionScrollRect.viewport;
        if (viewport == null)
            return;

        Canvas.ForceUpdateCanvases();

        float viewportWidth = viewport.rect.width;
        float viewportHeight = viewport.rect.height;
        float textWidth = viewportWidth - descriptionHorizontalPadding * 2f;

        descriptionContent.anchorMin = new Vector2(0f, 1f);
        descriptionContent.anchorMax = new Vector2(1f, 1f);
        descriptionContent.pivot = new Vector2(0.5f, 1f);
        descriptionContent.anchoredPosition = Vector2.zero;
        descriptionContent.sizeDelta = new Vector2(0f, viewportHeight);

        RectTransform textRect = artworkDescription.rectTransform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.pivot = new Vector2(0.5f, 0.5f);
        textRect.offsetMin = new Vector2(descriptionHorizontalPadding, 0f);
        textRect.offsetMax = new Vector2(-descriptionHorizontalPadding, 0f);

        artworkDescription.ForceMeshUpdate(true);
        float textHeight = artworkDescription.GetPreferredValues(
            artworkDescription.text,
            textWidth,
            0f).y;

        float contentHeight = Mathf.Max(textHeight, viewportHeight);
        descriptionContent.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, contentHeight);

        LayoutRebuilder.ForceRebuildLayoutImmediate(descriptionContent);
        descriptionScrollRect.verticalNormalizedPosition = 1f;
    }

    private void CloseDescriptionPanel()
    {
        artworkDescription.text = string.Empty;
        artworkTitle.text = string.Empty;

        descriptionPanel.SetActive(false);
        descriptionCloseButton.gameObject.SetActive(false);
        descriptionOpenButton.gameObject.SetActive(true);

        chatCloseButton.SetActive(false);
        chatOpenButton.SetActive(true);

        SetCameraPaused(false);
        ClearFrozenFrame();
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

    private void SetCameraPaused(bool paused)
    {
        // if (VuforiaBehaviour.Instance == null)
        //     return;

        // VuforiaBehaviour.Instance.enabled = !paused;
    }
}
