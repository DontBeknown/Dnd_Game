using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static ASAL.PlayHudControls;

namespace ASAL
{
    public sealed class PlayChatHud : MonoBehaviour
    {
        [SerializeField] private GameObject chatPanel, chatRow;
        [SerializeField] private Button chat, sendChat;
        [SerializeField] private TMP_InputField chatInput;
        [SerializeField] private TMP_Text chatStatus;
        [SerializeField] private ScrollRect chatScroll;

        internal void Initialize(PlayHud hud)
        {
            chatPanel.SetActive(false);
            chatRow.SetActive(false);
            chat.onClick.AddListener(() => hud.ToggleModal(chatPanel, chat, FocusChat, selectFirstControl: false));
            sendChat.onClick.AddListener(SendChat);
            chatInput.onSubmit.AddListener(_ => SendChat());
            ChatManager.Instance.Changed += RenderChat;
            RenderChat();
        }

        internal void Release()
        {
            if (ChatManager.Instance)
                ChatManager.Instance.Changed -= RenderChat;
        }

        private void FocusChat()
        {
            RenderChat();
            chatInput.Select();
            chatInput.ActivateInputField();
        }

        private void SendChat()
        {
            if (string.IsNullOrWhiteSpace(chatInput.text))
                return;
            if (!ChatManager.Instance.Send(chatInput.text))
            {
                SetText(chatStatus, "Could not send the message.");
                return;
            }
            SetText(chatStatus, "");
            chatInput.text = "";
            chatInput.ActivateInputField();
        }

        private void RenderChat()
        {
            ClearRows(chatRow);
            foreach (var entry in ChatManager.Instance.History)
            {
                var row = AddRow(chatRow);
                var label = row.GetComponentInChildren<TMP_Text>(true);
                SetText(label, entry);
                var rect = (RectTransform)row.transform;
                var size = rect.sizeDelta;
                size.y = label.GetPreferredValues(entry, size.x - 16, 0).y + 24;
                rect.sizeDelta = size;
            }
            if (chatPanel.activeInHierarchy)
            {
                Canvas.ForceUpdateCanvases();
                chatScroll.verticalNormalizedPosition = 0;
            }
        }
    }
}
