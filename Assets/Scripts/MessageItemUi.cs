using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class MessageItemUi : MonoBehaviour
{
    [Header("Row")]
    [SerializeField] private RectTransform rowRect;
    [SerializeField] private HorizontalLayoutGroup rowLayout;
    [SerializeField] private LayoutElement rowLayoutElement;

    [Header("Bubble")]
    [SerializeField] private RectTransform bubbleRect;
    [SerializeField] private LayoutElement bubbleLayoutElement;

    [Header("Text")]
    [SerializeField] private TMP_Text chatText;

    [SerializeField] private float horizontalPadding = 32f;
    [SerializeField] private float verticalPadding = 24f;

    public void SetMessage(string message, float maxBubbleWidth, bool isMine)
    {
        PrepareRow(isMine);
        chatText.text = message;
        chatText.maxVisibleCharacters = int.MaxValue;

        Vector2 size = MeasureBubble(message, maxBubbleWidth);
        SetBubbleDimensions(size.x, size.y);
    }

    public IEnumerator PlayTypewriter(
        string message,
        float maxBubbleWidth,
        bool isMine,
        float charDelay,
        Action onCharacterShown = null)
    {
        PrepareRow(isMine);
        chatText.text = message;
        chatText.maxVisibleCharacters = 0;

        // Lock the final width up-front so word wrapping stays stable,
        // then grow only the height as lines are revealed.
        Vector2 finalSize = MeasureBubble(message, maxBubbleWidth);
        float bubbleWidth = finalSize.x;
        float oneLineHeight = chatText.fontSize * 1.2f + verticalPadding;
        SetBubbleDimensions(bubbleWidth, oneLineHeight);

        chatText.ForceMeshUpdate(true);
        int totalCharacters = chatText.textInfo.characterCount;

        for (int i = 1; i <= totalCharacters; i++)
        {
            chatText.maxVisibleCharacters = i;
            chatText.ForceMeshUpdate(true);

            // TMP's preferredHeight ignores maxVisibleCharacters, so compute
            // the height of the visible lines from the rendered text info.
            TMP_TextInfo info = chatText.textInfo;
            int lastVisible = Mathf.Min(i, info.characterCount) - 1;

            if (lastVisible >= 0)
            {
                int lastLine = info.characterInfo[lastVisible].lineNumber;
                float visibleHeight = info.lineInfo[0].ascender - info.lineInfo[lastLine].descender;
                float bubbleHeight = Mathf.Max(visibleHeight + verticalPadding, oneLineHeight);
                SetBubbleDimensions(bubbleWidth, bubbleHeight);
            }

            onCharacterShown?.Invoke();

            if (charDelay > 0f)
                yield return new WaitForSeconds(charDelay);
            else
                yield return null;
        }

        chatText.maxVisibleCharacters = int.MaxValue;
        SetBubbleDimensions(finalSize.x, finalSize.y);
        onCharacterShown?.Invoke();
    }

    private void PrepareRow(bool isMine)
    {
        rowLayout.childAlignment = isMine
            ? TextAnchor.UpperRight
            : TextAnchor.UpperLeft;

        // The prefab row is authored at a fixed width (900) and the parent
        // layout group does not control child widths. Match the row to the
        // actual content width so bubbles can never overflow the screen.
        if (transform.parent is RectTransform parentRect && parentRect.rect.width > 0f)
            rowRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, parentRect.rect.width);
    }

    private Vector2 MeasureBubble(string message, float maxBubbleWidth)
    {
        if (string.IsNullOrEmpty(message))
            message = " ";

        float maxTextWidth = maxBubbleWidth - horizontalPadding;
        Vector2 preferred = chatText.GetPreferredValues(message, maxTextWidth, 0f);

        // Small buffer avoids an unwanted extra line-wrap caused by rounding.
        float bubbleWidth = Mathf.Min(preferred.x + horizontalPadding + 2f, maxBubbleWidth);
        float textWidth = bubbleWidth - horizontalPadding;

        float textHeight = chatText.GetPreferredValues(message, textWidth, 0f).y;
        float bubbleHeight = textHeight + verticalPadding;

        return new Vector2(bubbleWidth, bubbleHeight);
    }

    private void SetBubbleDimensions(float bubbleWidth, float bubbleHeight)
    {
        bubbleLayoutElement.preferredWidth = bubbleWidth;
        bubbleLayoutElement.preferredHeight = bubbleHeight;
        bubbleLayoutElement.minWidth = bubbleWidth;
        bubbleLayoutElement.minHeight = bubbleHeight;

        bubbleRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, bubbleWidth);
        bubbleRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, bubbleHeight);

        rowLayoutElement.preferredHeight = bubbleHeight;
        rowLayoutElement.minHeight = bubbleHeight;

        rowRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, bubbleHeight);

        LayoutRebuilder.ForceRebuildLayoutImmediate(rowRect);

        if (transform.parent is RectTransform parentRect)
            LayoutRebuilder.ForceRebuildLayoutImmediate(parentRect);
    }
}
