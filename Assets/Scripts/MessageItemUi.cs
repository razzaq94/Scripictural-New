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
        SetupBubble(message, maxBubbleWidth, isMine);
        chatText.maxVisibleCharacters = int.MaxValue;
    }

    public IEnumerator PlayTypewriter(
        string message,
        float maxBubbleWidth,
        bool isMine,
        float charDelay,
        Action onCharacterShown = null)
    {
        SetupBubble(message, maxBubbleWidth, isMine);

        chatText.maxVisibleCharacters = 0;
        chatText.ForceMeshUpdate();

        int totalCharacters = chatText.textInfo.characterCount;

        for (int i = 0; i <= totalCharacters; i++)
        {
            chatText.maxVisibleCharacters = i;
            onCharacterShown?.Invoke();

            if (charDelay > 0f)
                yield return new WaitForSeconds(charDelay);
            else
                yield return null;
        }

        chatText.maxVisibleCharacters = int.MaxValue;
    }

    private void SetupBubble(string message, float maxBubbleWidth, bool isMine)
    {
        chatText.text = message;

        rowLayout.childAlignment = isMine
            ? TextAnchor.UpperRight
            : TextAnchor.UpperLeft;

        float maxTextWidth = maxBubbleWidth - horizontalPadding;
        Vector2 preferred = chatText.GetPreferredValues(message, maxTextWidth, 0f);

        float bubbleWidth = Mathf.Min(preferred.x + horizontalPadding, maxBubbleWidth);
        float textWidth = bubbleWidth - horizontalPadding;

        float textHeight = chatText.GetPreferredValues(message, textWidth, 0f).y;
        float bubbleHeight = textHeight + verticalPadding;

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
    }
}