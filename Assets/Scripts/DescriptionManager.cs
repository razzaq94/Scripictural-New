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

    [SerializeField] private RawImage frozenCameraImage;

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
    }

    public void AddDescription(string msg)
    {
        cachedDescription = msg;
    }

    public void AddTitle(string title)
    {
        cachedTitle = title;
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

        isOpening = false;
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
        ClearFrozenFrame();

        frozenFrameTexture = ScreenCapture.CaptureScreenshotAsTexture();

        if (frozenCameraImage == null)
            return;

        frozenCameraImage.texture = frozenFrameTexture;
        frozenCameraImage.gameObject.SetActive(true);
    }

    private void ClearFrozenFrame()
    {
        if (frozenCameraImage != null)
        {
            frozenCameraImage.texture = null;
            frozenCameraImage.gameObject.SetActive(false);
        }

        if (frozenFrameTexture != null)
        {
            Destroy(frozenFrameTexture);
            frozenFrameTexture = null;
        }
    }

    private void SetCameraPaused(bool paused)
    {
        if (VuforiaBehaviour.Instance == null)
            return;

        VuforiaBehaviour.Instance.enabled = !paused;
    }
}