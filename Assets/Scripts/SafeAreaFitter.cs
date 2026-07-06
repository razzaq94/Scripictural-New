using UnityEngine;

/// <summary>
/// Insets this RectTransform to the device safe area (notch, punch-hole,
/// rounded corners, home indicator). Attach to a full-stretch child of the
/// canvas and parent all UI that must stay reachable under it.
/// </summary>
[RequireComponent(typeof(RectTransform))]
[ExecuteAlways]
public class SafeAreaFitter : MonoBehaviour
{
    private RectTransform rectTransform;
    private Rect lastSafeArea = Rect.zero;
    private Vector2Int lastScreenSize = Vector2Int.zero;
    private ScreenOrientation lastOrientation = ScreenOrientation.AutoRotation;

    private void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
        ApplySafeArea();
    }

    private void Update()
    {
        // Re-apply when the safe area changes (rotation, foldables, etc).
        if (Screen.safeArea != lastSafeArea ||
            Screen.width != lastScreenSize.x ||
            Screen.height != lastScreenSize.y ||
            Screen.orientation != lastOrientation)
        {
            ApplySafeArea();
        }
    }

    private void ApplySafeArea()
    {
        if (rectTransform == null)
            rectTransform = GetComponent<RectTransform>();

        if (Screen.width <= 0 || Screen.height <= 0)
            return;

        Rect safeArea = Screen.safeArea;
        lastSafeArea = safeArea;
        lastScreenSize = new Vector2Int(Screen.width, Screen.height);
        lastOrientation = Screen.orientation;

        Vector2 anchorMin = safeArea.position;
        Vector2 anchorMax = safeArea.position + safeArea.size;
        anchorMin.x /= Screen.width;
        anchorMin.y /= Screen.height;
        anchorMax.x /= Screen.width;
        anchorMax.y /= Screen.height;

        rectTransform.anchorMin = anchorMin;
        rectTransform.anchorMax = anchorMax;
        rectTransform.offsetMin = Vector2.zero;
        rectTransform.offsetMax = Vector2.zero;
    }
}
