using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ChatManager : MonoBehaviour
{
    [SerializeField] private GameObject chatParent;
    [SerializeField] private TMP_InputField textInput;
    [SerializeField] private Button submitButton;
    [SerializeField] private Transform content;

    [SerializeField] MessageItemUi myMessageItem;
    [SerializeField] MessageItemUi responseMessageItem;

    private void Start()
    {
        submitButton.onClick.AddListener(OnSubmitClicked);
    }

    private void OnDestroy()
    {
        submitButton.onClick.RemoveListener(OnSubmitClicked);
    }

    private void OnEnable()
    {
        foreach (Transform child in content)
        {
            Destroy(child.gameObject);
        }
    }

    private void OnSubmitClicked()
    {
        string message = textInput.text.Trim();

        if (string.IsNullOrEmpty(message))
            return;

        float contentWidth = ((RectTransform)content).rect.width;
        float maxBubbleWidth = contentWidth * 0.8f;

        MessageItemUi msg = Instantiate(myMessageItem, content);
        msg.SetMessage(message, maxBubbleWidth, true);

        textInput.text = "";
        textInput.ActivateInputField();
        StartCoroutine(testing());
    }
    IEnumerator testing()
    {
        yield return new WaitForSeconds(1);
        AddResponseMessage("jnkjasn adsnansc adncas d mcndsamcsa dcadsncmsac cjasnfrujfnasjvads adsmc asmc , mlml m , mlaml lmakll l ml ml ml cjln landsd gijer hgiqwhurgdnmagkjds vkdak");
    }
    private void AddResponseMessage(string message)
    {
        float contentWidth = ((RectTransform)content).rect.width;
        float maxBubbleWidth = contentWidth * 0.8f;

        MessageItemUi msg = Instantiate(responseMessageItem, content);
        msg.SetMessage(message, maxBubbleWidth, false);
    }
}
