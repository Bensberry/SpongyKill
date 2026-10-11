using Spongykill.Core;
using Spongykill.Net;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Spongykill.UI
{
    /// <summary>
    /// The title screen: play against bots, Quick Match online (Edgegap matchmaker), join a server by
    /// address, host a LAN game, set your name. Built from code at start.
    /// </summary>
    public class MainMenu : MonoBehaviour
    {
        static readonly string[] DifficultyNames = { "Easy", "Normal", "Hard" };
        static readonly Color Title = new Color(1f, 0.86f, 0.3f);

        Canvas menuCanvas;
        TextMeshProUGUI status;
        Button cancelButton;
        Button[] actionButtons;
        GameObject joinPanel;
        TMP_InputField nameField;
        TMP_InputField addressField;
        TMP_InputField portField;
        Selector difficulty;
        EdgegapQuickMatch quickMatch;
        bool busy;

        void Start()
        {
            UiKit.EnsureEventSystem();
            CursorLock.Unlock();
            Time.timeScale = 1f;
            InputGate.MenuOpen = false;
            InputGate.MatchFrozen = false;
            Build();

            if (!string.IsNullOrEmpty(NetSession.LastDisconnectReason))
            {
                SetStatus(NetSession.LastDisconnectReason, UiKit.Danger);
                NetSession.LastDisconnectReason = null;
            }
            else
            {
                SetStatus("", Color.white);
            }
        }

        void Update()
        {
            NetSession session = NetSession.Instance;
            bool connecting = session != null && session.IsConnecting;
            bool nowBusy = quickMatch != null || connecting;
            if (nowBusy != busy) SetBusy(nowBusy);
        }

        void Build()
        {
            Canvas canvas = UiKit.CreateCanvas("MainMenuUI", 10, transform);
            menuCanvas = canvas;
            // The 3D backdrop shows through; a darker band keeps the menu readable.
            Image band = UiKit.Image(canvas.transform, "Band", new Color(0.02f, 0.1f, 0.17f, 0.78f));
            UiKit.Anchor(band.rectTransform, Vector2.zero, new Vector2(0f, 1f), Vector2.zero, new Vector2(960f, 0f));
            RectTransform safe = UiKit.Stretch(UiKit.Rect(canvas.transform, "SafeArea"));
            safe.gameObject.AddComponent<SafeAreaFitter>();

            // The game's logo stands over the backdrop on the right; plain text if it's missing.
            var logo = Resources.Load<Sprite>("Logo");
            if (logo != null)
            {
                Image logoImage = UiKit.Image(safe, "Logo", Color.white, logo);
                logoImage.preserveAspect = true;
                UiKit.Place(logoImage.rectTransform, new Vector2(0.73f, 0.5f), new Vector2(0f, 20f), new Vector2(620f, 720f));
                logoImage.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            }
            TextMeshProUGUI title = UiKit.Label(safe, "Title", "SPONGYKILL OPS", 110f, TextAlignmentOptions.TopLeft, Title, FontStyles.Bold);
            UiKit.Anchor(title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(110f, -190f), new Vector2(-110f, -60f));
            TextMeshProUGUI subtitle = UiKit.Label(safe, "Subtitle", "Team deathmatch under the sea  ·  5 v 5  ·  first to 50", 32f, TextAlignmentOptions.TopLeft, UiKit.TextDim, FontStyles.Bold);
            UiKit.Anchor(subtitle.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(116f, -240f), new Vector2(-110f, -190f));

            // Left column: name and the ways to play.
            RectTransform column = UiKit.Place(UiKit.Rect(safe, "Column"), new Vector2(0f, 1f), new Vector2(110f, -270f), new Vector2(640f, 760f));
            VerticalLayoutGroup layout = column.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 14f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            UiKit.Size(UiKit.Label(column, "NameLabel", "YOUR NAME", 22f, TextAlignmentOptions.BottomLeft, UiKit.TextDim, FontStyles.Bold), 30f);
            nameField = UiKit.InputField(column, "Name", NetSession.LocalName, "Enter a name", 16, value => NetSession.LocalName = value);
            UiKit.Size(nameField, 70f);

            Button vsBots = UiKit.Button(column, "PlayVsBots", "PLAY VS BOTS", PlayVsBots, UiKit.Selected, 36f);
            UiKit.Size(vsBots, 92f);
            HorizontalLayoutGroup difficultyRow = UiKit.Row(column, "Difficulty", 12f, 60f);
            UiKit.Size(UiKit.Label(difficultyRow.transform, "Label", "Bots", 24f, TextAlignmentOptions.Left, UiKit.TextDim, FontStyles.Bold), 60f, 110f, 0f);
            difficulty = UiKit.Selector(difficultyRow.transform, "Selector", DifficultyNames, NetSession.BotDifficultyLevel, level => NetSession.BotDifficultyLevel = level);
            UiKit.Size(difficulty, 60f, 400f, 1f);

            Button online = UiKit.Button(column, "QuickMatch", "QUICK MATCH  (ONLINE)", QuickMatch, UiKit.PanelRaised, 32f);
            UiKit.Size(online, 86f);
            Button join = UiKit.Button(column, "JoinByAddress", "JOIN BY ADDRESS", ToggleJoinPanel, UiKit.PanelRaised, 30f);
            UiKit.Size(join, 76f);
            Button settings = UiKit.Button(column, "Settings", "SETTINGS", () => SettingsScreen.Open(menuCanvas.transform), UiKit.PanelRaised, 30f);
            UiKit.Size(settings, 76f);
            Button quit = UiKit.Button(column, "Quit", "QUIT", GameMenus.QuitGame, UiKit.PanelRaised, 30f);
            UiKit.Size(quit, 76f);
            actionButtons = new[] { vsBots, online, join, settings, quit };

            BuildJoinPanel(safe);

            // Status line and cancel, bottom left.
            status = UiKit.Label(safe, "Status", "", 28f, TextAlignmentOptions.Left, Color.white, FontStyles.Bold);
            UiKit.Anchor(status.rectTransform, Vector2.zero, new Vector2(1f, 0f), new Vector2(116f, 40f), new Vector2(-480f, 110f));
            status.textWrappingMode = TextWrappingModes.Normal;
            cancelButton = UiKit.Button(safe, "Cancel", "CANCEL", Cancel, UiKit.PanelRaised, 28f);
            UiKit.Place((RectTransform)cancelButton.transform, new Vector2(1f, 0f), new Vector2(-110f, 40f), new Vector2(300f, 76f));
            cancelButton.gameObject.SetActive(false);

            TextMeshProUGUI version = UiKit.Label(safe, "Version", $"v{Application.version}  ·  net {NetSession.ProtocolVersion}", 20f, TextAlignmentOptions.TopRight, UiKit.TextDim);
            UiKit.Anchor(version.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(110f, -60f), new Vector2(-40f, -20f));
        }

        void BuildJoinPanel(RectTransform parent)
        {
            Image panel = UiKit.Image(parent, "JoinPanel", UiKit.Panel, UiKit.Rounded);
            UiKit.Place(panel.rectTransform, new Vector2(1f, 1f), new Vector2(-110f, -280f), new Vector2(620f, 470f));
            joinPanel = panel.gameObject;
            RectTransform content = UiKit.Anchor(UiKit.Rect(panel.transform, "Content"), Vector2.zero, Vector2.one, new Vector2(32f, 28f), new Vector2(-32f, -28f));
            VerticalLayoutGroup layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 12f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            OnlineConfig config = OnlineConfig.Instance;
            UiKit.Size(UiKit.Label(content, "Heading", "JOIN A SERVER", 30f, TextAlignmentOptions.Left, Color.white, FontStyles.Bold), 44f);
            UiKit.Size(UiKit.Label(content, "AddressLabel", "ADDRESS  (IP or host name)", 20f, TextAlignmentOptions.BottomLeft, UiKit.TextDim, FontStyles.Bold), 26f);
            addressField = UiKit.InputField(content, "Address", config ? config.lastAddress : "127.0.0.1", "127.0.0.1", 64);
            UiKit.Size(addressField, 64f);
            UiKit.Size(UiKit.Label(content, "PortLabel", "PORT", 20f, TextAlignmentOptions.BottomLeft, UiKit.TextDim, FontStyles.Bold), 26f);
            portField = UiKit.InputField(content, "Port", (config ? config.lastPort : NetSession.DefaultPort).ToString(), NetSession.DefaultPort.ToString(), 5);
            portField.contentType = TMP_InputField.ContentType.IntegerNumber;
            UiKit.Size(portField, 64f);
            HorizontalLayoutGroup buttons = UiKit.Row(content, "Buttons", 14f, 76f);
            buttons.childForceExpandWidth = true;
            UiKit.Button(buttons.transform, "Join", "JOIN", JoinAddress, UiKit.Selected, 30f);
            UiKit.Button(buttons.transform, "HostLan", "HOST LAN GAME", HostLan, UiKit.PanelRaised, 26f);
            joinPanel.SetActive(false);
        }

        // ------------------------------------------------------------------ Actions

        void PlayVsBots()
        {
            if (!Ready()) return;
            SetStatus("Starting...", Color.white);
            if (!NetSession.Instance.StartVsBots(difficulty.Index)) SetStatus(NetSession.LastDisconnectReason, UiKit.Danger);
        }

        void QuickMatch()
        {
            if (!Ready()) return;
            quickMatch = EdgegapQuickMatch.Begin(gameObject,
                text => SetStatus(text, Color.white),
                (address, port, ticket) =>
                {
                    quickMatch = null;
                    if (!NetSession.Instance.Join(address, port, ticket)) SetStatus(NetSession.LastDisconnectReason, UiKit.Danger);
                    else SetStatus($"Connecting to {address}:{port}...", Color.white);
                },
                reason =>
                {
                    quickMatch = null;
                    SetStatus(reason, UiKit.Danger);
                });
        }

        void ToggleJoinPanel() => joinPanel.SetActive(!joinPanel.activeSelf);

        void JoinAddress()
        {
            if (!Ready()) return;
            string address = string.IsNullOrWhiteSpace(addressField.text) ? "127.0.0.1" : addressField.text.Trim();
            if (!ushort.TryParse(portField.text, out ushort port)) port = NetSession.DefaultPort;
            OnlineConfig config = OnlineConfig.Instance;
            if (config)
            {
                config.lastAddress = address;
                config.lastPort = port;
            }
            if (NetSession.Instance.Join(address, port)) SetStatus($"Connecting to {address}:{port}...", Color.white);
            else SetStatus(NetSession.LastDisconnectReason, UiKit.Danger);
        }

        void HostLan()
        {
            if (!Ready()) return;
            if (!NetSession.Instance.StartLanHost()) SetStatus(NetSession.LastDisconnectReason, UiKit.Danger);
        }

        void Cancel()
        {
            if (quickMatch != null)
            {
                quickMatch.Cancel();
                quickMatch = null;
                SetStatus("Matchmaking cancelled.", UiKit.TextDim);
                return;
            }
            if (NetSession.Instance != null && NetSession.Instance.IsConnecting) NetSession.Instance.Leave("Connection cancelled.");
        }

        bool Ready()
        {
            if (busy) return false;
            if (nameField) NetSession.LocalName = nameField.text;
            if (NetSession.Instance != null) return true;
            SetStatus("Networking isn't set up in this scene (rebuild the main menu).", UiKit.Danger);
            return false;
        }

        void SetBusy(bool value)
        {
            busy = value;
            foreach (Button button in actionButtons) button.interactable = !value;
            cancelButton.gameObject.SetActive(value);
        }

        void SetStatus(string text, Color color)
        {
            status.text = text ?? "";
            status.color = color;
        }
    }
}
