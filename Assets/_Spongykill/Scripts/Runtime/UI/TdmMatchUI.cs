using System.Collections.Generic;
using Spongykill.Combat;
using Spongykill.Controls;
using Spongykill.Core;
using Spongykill.Match;
using Spongykill.Net;
using Spongykill.Weapons;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Spongykill.UI
{
    /// <summary>
    /// Screens for team deathmatch: the score bar and timer, the countdown, the loadout picker
    /// (which is how you join, then while dead or any time with B), the scoreboard (hold Tab or tap
    /// the touch button), an Escape menu to leave, and the results screen with the countdown to the
    /// next match. Built from code once the networked match object exists.
    /// </summary>
    public class TdmMatchUI : MonoBehaviour
    {
        TdmMatch match;
        Canvas canvas;
        bool built;

        TextMeshProUGUI friendlyScore;
        TextMeshProUGUI enemyScore;
        TextMeshProUGUI friendlyName;
        TextMeshProUGUI enemyName;
        TextMeshProUGUI timerText;
        TextMeshProUGUI limitText;
        RectTransform friendlyFill;
        RectTransform enemyFill;
        TextMeshProUGUI bigText;
        GameObject deathActions;
        TextMeshProUGUI deathHint;
        Team shownTeam = (Team)(-1);

        LoadoutScreen loadout;
        GameObject scoreboardRoot;
        StandingsTable scoreboard;
        bool scoreboardToggled;

        GameObject resultsRoot;
        TextMeshProUGUI resultsTitle;
        TextMeshProUGUI resultsScore;
        TextMeshProUGUI resultsDetail;
        TextMeshProUGUI resultsNext;
        Button playAgainButton;
        StandingsTable resultsTable;

        GameObject pauseRoot;
        readonly object pauseMenuKey = new object();

        int shownSeconds = -1;
        float fightTextUntil;
        bool wasDead;

        // On clients the match object spawns a little after this scene's Start, so build lazily.
        void Start() => TryBuild();

        void TryBuild()
        {
            if (built || TdmMatch.Current == null) return;
            built = true;
            match = TdmMatch.Current;
            InputGate.MenusPresent = true;
            UiKit.EnsureEventSystem();
            canvas = UiKit.CreateCanvas("MatchUI", 25, transform);
            RectTransform safe = UiKit.Stretch(UiKit.Rect(canvas.transform, "SafeArea"));
            safe.gameObject.AddComponent<SafeAreaFitter>();

            BuildScoreBar(safe);
            Minimap.Create(safe);
            TouchControls.CreateForGame(transform);
            bigText = UiKit.Label(safe, "BigText", "", 120f, TextAlignmentOptions.Center, Color.white, FontStyles.Bold);
            UiKit.Place(bigText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 120f), new Vector2(1400f, 160f));
            BuildDeathActions(safe);

            scoreboardRoot = UiKit.Image(canvas.transform, "Scoreboard", new Color(0f, 0f, 0f, 0.35f)).gameObject;
            UiKit.Stretch((RectTransform)scoreboardRoot.transform);
            RectTransform board = UiKit.Image(scoreboardRoot.transform, "Panel", UiKit.Panel, UiKit.Rounded).rectTransform;
            UiKit.Place(board, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1500f, 640f));
            board.gameObject.AddComponent<MaxSizeFitter>().maxSize = new Vector2(1500f, 640f);
            scoreboard = new StandingsTable(board, match);
            scoreboardRoot.SetActive(false);

            loadout = new LoadoutScreen(canvas.transform, match, OnLoadoutConfirmed);
            BuildResults(canvas.transform);
            BuildPauseMenu(canvas.transform);

            match.PhaseChanged += OnPhaseChanged;
            match.ScoreChanged += RefreshScores;
            RefreshScores();
            OnPhaseChanged();
        }

        void OnDestroy()
        {
            InputGate.MenusPresent = false;
            if (match == null) return;
            match.PhaseChanged -= OnPhaseChanged;
            match.ScoreChanged -= RefreshScores;
            MenuState.Pop(loadout);
            MenuState.Pop(this);
            MenuState.Pop(pauseMenuKey);
        }

        void Update()
        {
            if (!built)
            {
                TryBuild();
                if (!built) return;
            }
            if (match == null) return;
            if (match.PlayerTeam != shownTeam) RefreshTeams();
            UpdateTimer();
            UpdateBigText();
            UpdateDeathActions();
            UpdateScoreboard();
            UpdateResults();

            // Before the first deploy the loadout screen stays up; it is how you join the fight.
            if (!match.HasLocalPlayer && !match.DeployPending && match.Phase != MatchPhase.Ended && !MenuState.AnyOpen) OpenLoadout();

            Keyboard keyboard = Keyboard.current;
            bool pauseKey = (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) || TouchInput.Pressed(TouchButton.Pause);
            if (pauseKey) TogglePause();
            bool loadoutKey = keyboard != null && keyboard.bKey.wasPressedThisFrame;
            if (loadoutKey && match.Phase == MatchPhase.Live && !MenuState.AnyOpen) OpenLoadout();
        }

        // ------------------------------------------------------------------ Score bar

        void BuildScoreBar(RectTransform parent)
        {
            RectTransform bar = UiKit.Place(UiKit.Rect(parent, "ScoreBar"), new Vector2(0.5f, 1f), new Vector2(0f, -14f), new Vector2(660f, 92f));
            (friendlyName, friendlyScore, friendlyFill) = ScoreBox(bar, "Friendly", new Vector2(0f, 1f));
            (enemyName, enemyScore, enemyFill) = ScoreBox(bar, "Enemy", new Vector2(1f, 1f));

            Image timerBack = UiKit.Image(bar, "Timer", UiKit.Panel, UiKit.Rounded);
            UiKit.Place(timerBack.rectTransform, new Vector2(0.5f, 1f), Vector2.zero, new Vector2(150f, 64f));
            timerText = UiKit.Label(timerBack.transform, "Text", "8:00", 40f, TextAlignmentOptions.Center, Color.white, FontStyles.Bold);
            UiKit.Stretch(timerText.rectTransform);
            limitText = UiKit.Label(bar, "Limit", $"FIRST TO {match.ScoreLimit}", 20f, TextAlignmentOptions.Center, UiKit.TextDim, FontStyles.Bold);
            UiKit.Place(limitText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -66f), new Vector2(300f, 26f));
            RefreshTeams();
        }

        static (TextMeshProUGUI name, TextMeshProUGUI score, RectTransform fill) ScoreBox(RectTransform bar, string boxName, Vector2 anchor)
        {
            Image back = UiKit.Image(bar, boxName, UiKit.Panel, UiKit.Rounded);
            UiKit.Place(back.rectTransform, anchor, Vector2.zero, new Vector2(244f, 64f));
            bool left = anchor.x < 0.5f;
            TextMeshProUGUI score = UiKit.Label(back.transform, "Score", "0", 44f, left ? TextAlignmentOptions.Right : TextAlignmentOptions.Left, Color.white, FontStyles.Bold);
            UiKit.Anchor(score.rectTransform, Vector2.zero, Vector2.one, new Vector2(14f, 4f), new Vector2(-14f, 0f));
            TextMeshProUGUI name = UiKit.Label(back.transform, "Name", "", 17f, left ? TextAlignmentOptions.BottomLeft : TextAlignmentOptions.BottomRight, Color.white, FontStyles.Bold);
            UiKit.Anchor(name.rectTransform, Vector2.zero, Vector2.one, new Vector2(14f, 8f), new Vector2(-14f, 0f));

            Image track = UiKit.Image(back.transform, "Track", new Color(0f, 0f, 0f, 0.4f));
            UiKit.Anchor(track.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(10f, -10f), new Vector2(-10f, -4f));
            Image fill = UiKit.Image(track.transform, "Fill", Color.white);
            UiKit.Stretch(fill.rectTransform);
            fill.rectTransform.pivot = new Vector2(left ? 1f : 0f, 0.5f);
            fill.rectTransform.localScale = new Vector3(0f, 1f, 1f);
            return (name, score, fill.rectTransform);
        }

        /// <summary>Your team is always on the left in your team's colour.</summary>
        void RefreshTeams()
        {
            shownTeam = match.PlayerTeam;
            Color friendly = TeamColors.For(shownTeam);
            Color enemy = TeamColors.For(Other(shownTeam));
            friendlyName.text = TeamColors.NameOf(shownTeam).ToUpperInvariant();
            enemyName.text = TeamColors.NameOf(Other(shownTeam)).ToUpperInvariant();
            friendlyName.color = friendlyScore.color = friendly;
            enemyName.color = enemyScore.color = enemy;
            friendlyFill.GetComponent<Image>().color = friendly;
            enemyFill.GetComponent<Image>().color = enemy;
            RefreshScores();
        }

        void RefreshScores()
        {
            int ours = match.Score(match.PlayerTeam);
            int theirs = match.Score(Other(match.PlayerTeam));
            friendlyScore.SetText("{0}", ours);
            enemyScore.SetText("{0}", theirs);
            friendlyFill.localScale = new Vector3(Mathf.Clamp01((float)ours / match.ScoreLimit), 1f, 1f);
            enemyFill.localScale = new Vector3(Mathf.Clamp01((float)theirs / match.ScoreLimit), 1f, 1f);
            if (scoreboardRoot && scoreboardRoot.activeSelf) scoreboard.Refresh();
        }

        void UpdateTimer()
        {
            int seconds = Mathf.CeilToInt(match.TimeRemaining);
            if (seconds == shownSeconds) return;
            shownSeconds = seconds;
            timerText.SetText("{0}:{1:00}", (float)(seconds / 60), (float)(seconds % 60));
            timerText.color = seconds <= 30 && match.Phase == MatchPhase.Live ? UiKit.Danger : Color.white;
        }

        void UpdateBigText()
        {
            string text = "";
            if (match.Phase == MatchPhase.Countdown) text = Mathf.CeilToInt(match.CountdownRemaining).ToString();
            else if (Time.time < fightTextUntil) text = "FIGHT!";
            if (bigText.text != text) bigText.text = text;
        }

        // ------------------------------------------------------------------ Death and loadout

        void BuildDeathActions(RectTransform parent)
        {
            RectTransform root = UiKit.Place(UiKit.Rect(parent, "DeathActions"), new Vector2(0.5f, 0.5f), new Vector2(0f, -170f), new Vector2(520f, 120f));
            deathActions = root.gameObject;
            Button button = UiKit.Button(root, "ChangeLoadout", "CHANGE LOADOUT", OpenLoadout, UiKit.PanelRaised, 30f);
            UiKit.Place((RectTransform)button.transform, new Vector2(0.5f, 1f), Vector2.zero, new Vector2(420f, 76f));
            deathHint = UiKit.Label(root, "Hint", "", 22f, TextAlignmentOptions.Center, UiKit.TextDim);
            UiKit.Place(deathHint.rectTransform, new Vector2(0.5f, 0f), Vector2.zero, new Vector2(520f, 34f));
            deathActions.SetActive(false);
        }

        void UpdateDeathActions()
        {
            TeamMember player = TeamMember.LocalPlayer;
            bool dead = player != null && !player.IsAlive && match.Phase == MatchPhase.Live;
            if (dead && !wasDead && !ControlScheme.UseTouch) CursorLock.Unlock();
            wasDead = dead;

            bool show = dead && !MenuState.AnyOpen;
            if (deathActions.activeSelf != show) deathActions.SetActive(show);
            if (show) deathHint.text = ControlScheme.UseTouch ? "Pick your weapons for the next life" : "or press  B";
        }

        void OpenLoadout()
        {
            if (match.Phase == MatchPhase.Ended) return;
            bool dead = TeamMember.LocalPlayer != null && !TeamMember.LocalPlayer.IsAlive;
            if (dead) match.HoldPlayerRespawn = true;
            bool deploy = !match.HasLocalPlayer || dead;
            loadout.Open(deploy ? "DEPLOY" : "SAVE  (NEXT LIFE)", !match.HasLocalPlayer);
        }

        void OnLoadoutConfirmed() => match.Deploy();

        // ------------------------------------------------------------------ Pause (Esc)

        void BuildPauseMenu(Transform parent)
        {
            Image backdrop = UiKit.Image(parent, "Pause", UiKit.Backdrop, null, raycast: true);
            UiKit.Stretch(backdrop.rectTransform);
            pauseRoot = backdrop.gameObject;
            RectTransform panel = UiKit.Image(backdrop.transform, "Panel", UiKit.Panel, UiKit.Rounded).rectTransform;
            UiKit.Place(panel, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(560f, 610f));
            VerticalLayoutGroup layout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(40, 40, 36, 36);
            layout.spacing = 16f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            UiKit.Size(UiKit.Label(panel, "Title", "PAUSED", 48f, TextAlignmentOptions.Center, Color.white, FontStyles.Bold), 70f);
            UiKit.Size(UiKit.Button(panel, "Resume", "RESUME", TogglePause, UiKit.Selected, 32f), 84f);
            UiKit.Size(UiKit.Button(panel, "Loadout", "CHANGE LOADOUT", () =>
            {
                TogglePause();
                OpenLoadout();
            }, UiKit.PanelRaised, 30f), 80f);
            UiKit.Size(UiKit.Button(panel, "Settings", "SETTINGS", () => SettingsScreen.Open(canvas.transform), UiKit.PanelRaised, 30f), 80f);
            UiKit.Size(UiKit.Button(panel, "Leave", "LEAVE MATCH", Leave, UiKit.PanelRaised, 30f), 80f);
            UiKit.Size(UiKit.Label(panel, "Note", "Online matches keep running while this is open.", 20f, TextAlignmentOptions.Center, UiKit.TextDim), 50f);
            pauseRoot.SetActive(false);
        }

        void TogglePause()
        {
            if (pauseRoot.activeSelf)
            {
                pauseRoot.SetActive(false);
                MenuState.Pop(pauseMenuKey);
                return;
            }
            // Escape first closes whatever else is open.
            if (MenuState.IsOpen(loadout) && match.HasLocalPlayer)
            {
                loadout.Close();
                return;
            }
            if (MenuState.AnyOpen) return;
            pauseRoot.SetActive(true);
            MenuState.Push(pauseMenuKey);
        }

        static void Leave()
        {
            if (NetSession.Instance != null) NetSession.Instance.Leave();
            else GameMenus.QuitGame();
        }

        // ------------------------------------------------------------------ Scoreboard

        void UpdateScoreboard()
        {
            Keyboard keyboard = Keyboard.current;
            if (TouchInput.Pressed(TouchButton.Scoreboard)) scoreboardToggled = !scoreboardToggled;
            bool hold = keyboard != null && keyboard.tabKey.isPressed;
            bool show = (hold || scoreboardToggled) && match.Phase != MatchPhase.Ended && match.HasLocalPlayer && !MenuState.AnyOpen;
            if (scoreboardRoot.activeSelf == show) return;
            scoreboardRoot.SetActive(show);
            if (show) scoreboard.Refresh();
        }

        // ------------------------------------------------------------------ Results

        void BuildResults(Transform parent)
        {
            Image backdrop = UiKit.Image(parent, "Results", UiKit.Backdrop, null, raycast: true);
            UiKit.Stretch(backdrop.rectTransform);
            resultsRoot = backdrop.gameObject;
            RectTransform panel = UiKit.Image(backdrop.transform, "Panel", UiKit.Panel, UiKit.Rounded).rectTransform;
            UiKit.Place(panel, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1500f, 960f));
            panel.gameObject.AddComponent<MaxSizeFitter>().maxSize = new Vector2(1500f, 960f);

            resultsTitle = UiKit.Label(panel, "Title", "VICTORY", 96f, TextAlignmentOptions.Center, UiKit.Accent, FontStyles.Bold);
            UiKit.Anchor(resultsTitle.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -130f), new Vector2(0f, -20f));
            resultsScore = UiKit.Label(panel, "Score", "", 48f, TextAlignmentOptions.Center, Color.white, FontStyles.Bold);
            UiKit.Anchor(resultsScore.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -190f), new Vector2(0f, -130f));
            resultsDetail = UiKit.Label(panel, "Detail", "", 26f, TextAlignmentOptions.Center, UiKit.TextDim);
            UiKit.Anchor(resultsDetail.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -232f), new Vector2(0f, -192f));

            RectTransform table = UiKit.Anchor(UiKit.Rect(panel, "Table"), Vector2.zero, Vector2.one, new Vector2(30f, 140f), new Vector2(-30f, -250f));
            resultsTable = new StandingsTable(table, match);

            resultsNext = UiKit.Label(panel, "Next", "", 24f, TextAlignmentOptions.Center, UiKit.TextDim, FontStyles.Bold);
            UiKit.Anchor(resultsNext.rectTransform, Vector2.zero, new Vector2(1f, 0f), new Vector2(0f, 120f), new Vector2(0f, 150f));
            playAgainButton = UiKit.Button(panel, "PlayAgain", "PLAY AGAIN", () => match.RestartNow(), UiKit.Selected, 34f);
            UiKit.Place((RectTransform)playAgainButton.transform, new Vector2(0.5f, 0f), new Vector2(-200f, 30f), new Vector2(360f, 84f));
            Button leave = UiKit.Button(panel, "Leave", "LEAVE", Leave, UiKit.PanelRaised, 34f);
            UiKit.Place((RectTransform)leave.transform, new Vector2(0.5f, 0f), new Vector2(200f, 30f), new Vector2(360f, 84f));
            resultsRoot.SetActive(false);
        }

        void ShowResults()
        {
            Team ours = match.PlayerTeam;
            int friendly = match.Score(ours);
            int enemy = match.Score(Other(ours));
            bool draw = friendly == enemy;
            bool won = friendly > enemy;
            resultsTitle.text = draw ? "DRAW" : won ? "VICTORY" : "DEFEAT";
            resultsTitle.color = draw ? Color.white : won ? UiKit.Accent : UiKit.Danger;
            resultsScore.text = $"<color=#{ColorUtility.ToHtmlStringRGB(TeamColors.For(ours))}>{friendly}</color>   -   <color=#{ColorUtility.ToHtmlStringRGB(TeamColors.For(Other(ours)))}>{enemy}</color>";

            MemberStats best = null;
            foreach (Team team in new[] { Team.Attackers, Team.Defenders })
                foreach (MemberStats stats in match.Standings(team))
                    if (best == null || stats.Score > best.Score) best = stats;
            resultsDetail.text = best != null && best.Kills > 0 ? $"MVP: {best.Member.DisplayName}  ·  {best.Kills} kills" : match.MapName;

            // Only the machine running the match (the host) can skip the wait.
            playAgainButton.gameObject.SetActive(match.CanRestart);
            resultsTable.Refresh();
            if (pauseRoot.activeSelf) TogglePause();
            resultsRoot.SetActive(true);
            MenuState.Push(this);
        }

        void HideResults()
        {
            if (!resultsRoot.activeSelf) return;
            resultsRoot.SetActive(false);
            MenuState.Pop(this);
        }

        void UpdateResults()
        {
            if (!resultsRoot.activeSelf) return;
            resultsNext.text = $"Next match in {Mathf.CeilToInt(match.NextMatchRemaining)}";
        }

        void OnPhaseChanged()
        {
            switch (match.Phase)
            {
                case MatchPhase.Ended:
                    loadout.Close();
                    ShowResults();
                    break;
                case MatchPhase.Live:
                    HideResults();
                    fightTextUntil = Time.time + 1f;
                    break;
                default:
                    HideResults();
                    break;
            }
        }

        static Team Other(Team team) => team == Team.Attackers ? Team.Defenders : Team.Attackers;

        // ================================================================== Loadout screen

        sealed class LoadoutScreen
        {
            readonly TdmMatch match;
            readonly System.Action confirmed;
            readonly GameObject root;
            readonly TextMeshProUGUI subtitle;
            readonly TextMeshProUGUI confirmLabel;
            readonly TextMeshProUGUI detailName;
            readonly TextMeshProUGUI detailCategory;
            readonly TextMeshProUGUI detailAmmo;
            readonly (RectTransform fill, TextMeshProUGUI value)[] statBars = new (RectTransform, TextMeshProUGUI)[5];
            readonly List<Image>[] cards = { new List<Image>(), new List<Image>(), new List<Image>() };
            readonly int[] selection = new int[3];
            Button backButton;

            static readonly string[] StatNames = { "DAMAGE", "FIRE RATE", "ACCURACY", "RANGE", "MOBILITY" };

            public LoadoutScreen(Transform parent, TdmMatch match, System.Action confirmed)
            {
                this.match = match;
                this.confirmed = confirmed;

                Image backdrop = UiKit.Image(parent, "Loadout", UiKit.Backdrop, null, raycast: true);
                UiKit.Stretch(backdrop.rectTransform);
                root = backdrop.gameObject;
                RectTransform panel = UiKit.Image(backdrop.transform, "Panel", UiKit.Panel, UiKit.Rounded).rectTransform;
                UiKit.Place(panel, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1560f, 900f));
                panel.gameObject.AddComponent<MaxSizeFitter>().maxSize = new Vector2(1560f, 900f);

                TextMeshProUGUI title = UiKit.Label(panel, "Title", "CHOOSE YOUR LOADOUT", 56f, TextAlignmentOptions.TopLeft, Color.white, FontStyles.Bold);
                UiKit.Anchor(title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(44f, -100f), new Vector2(-44f, -30f));
                subtitle = UiKit.Label(panel, "Subtitle", "", 26f, TextAlignmentOptions.TopLeft, UiKit.TextDim);
                UiKit.Anchor(subtitle.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(46f, -140f), new Vector2(-44f, -100f));

                // Weapon choices on the left, details of the selected primary on the right.
                RectTransform choices = UiKit.Anchor(UiKit.Rect(panel, "Choices"), Vector2.zero, Vector2.one, new Vector2(44f, 150f), new Vector2(-560f, -160f));
                VerticalLayoutGroup column = choices.gameObject.AddComponent<VerticalLayoutGroup>();
                column.spacing = 12f;
                column.childControlWidth = true;
                column.childControlHeight = true;
                column.childForceExpandWidth = true;
                column.childForceExpandHeight = false;
                Section(choices, "PRIMARY", 0, match.Primaries);
                Section(choices, "SECONDARY", 1, match.Secondaries);
                Section(choices, "MELEE", 2, match.Melees);

                RectTransform details = UiKit.Image(panel, "Details", UiKit.PanelRaised, UiKit.Rounded).rectTransform;
                UiKit.Anchor(details, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-520f, 150f), new Vector2(-44f, -160f));
                detailName = UiKit.Label(details, "Name", "", 40f, TextAlignmentOptions.TopLeft, Color.white, FontStyles.Bold);
                UiKit.Anchor(detailName.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(28f, -76f), new Vector2(-28f, -22f));
                detailCategory = UiKit.Label(details, "Category", "", 24f, TextAlignmentOptions.TopLeft, UiKit.Accent, FontStyles.Bold);
                UiKit.Anchor(detailCategory.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(28f, -112f), new Vector2(-28f, -78f));
                for (int i = 0; i < statBars.Length; i++) statBars[i] = StatBar(details, StatNames[i], -150f - i * 66f);
                detailAmmo = UiKit.Label(details, "Ammo", "", 22f, TextAlignmentOptions.BottomLeft, UiKit.TextDim);
                UiKit.Anchor(detailAmmo.rectTransform, Vector2.zero, new Vector2(1f, 0f), new Vector2(28f, 20f), new Vector2(-28f, 84f));
                detailAmmo.textWrappingMode = TextWrappingModes.Normal;

                Button confirm = UiKit.Button(panel, "Confirm", "DEPLOY", () => Confirm(), UiKit.Selected, 38f);
                UiKit.Place((RectTransform)confirm.transform, new Vector2(1f, 0f), new Vector2(-44f, 36f), new Vector2(476f, 92f));
                confirmLabel = confirm.GetComponentInChildren<TextMeshProUGUI>();
                backButton = UiKit.Button(panel, "Back", "BACK", Close, UiKit.PanelRaised, 30f);
                UiKit.Place((RectTransform)backButton.transform, new Vector2(0f, 0f), new Vector2(44f, 36f), new Vector2(240f, 92f));
                root.SetActive(false);
            }

            void Section(RectTransform parent, string heading, int slot, IReadOnlyList<WeaponDefinition> weapons)
            {
                TextMeshProUGUI label = UiKit.Label(parent, heading, heading, 24f, TextAlignmentOptions.BottomLeft, UiKit.TextDim, FontStyles.Bold);
                UiKit.Size(label, 34f);
                HorizontalLayoutGroup row = UiKit.Row(parent, heading + "Cards", 14f, 116f);
                row.childForceExpandWidth = false;
                for (int i = 0; i < weapons.Count; i++)
                {
                    int index = i;
                    WeaponDefinition weapon = weapons[i];
                    Button card = UiKit.Button(row.transform, weapon.name, "", () => Select(slot, index), UiKit.PanelRaised);
                    UiKit.Size(card, 116f, 230f, 0f).minWidth = 140f;
                    TextMeshProUGUI name = UiKit.Label(card.transform, "Name", weapon.displayName, 26f, TextAlignmentOptions.TopLeft, Color.white, FontStyles.Bold);
                    UiKit.Anchor(name.rectTransform, Vector2.zero, Vector2.one, new Vector2(16f, 10f), new Vector2(-12f, -14f));
                    name.textWrappingMode = TextWrappingModes.Normal;
                    TextMeshProUGUI kind = UiKit.Label(card.transform, "Kind", weapon.category.ToString().ToUpperInvariant(), 18f, TextAlignmentOptions.BottomLeft, UiKit.TextDim, FontStyles.Bold);
                    UiKit.Anchor(kind.rectTransform, Vector2.zero, Vector2.one, new Vector2(16f, 12f), new Vector2(-12f, -12f));
                    cards[slot].Add(card.GetComponent<Image>());
                }
            }

            static (RectTransform, TextMeshProUGUI) StatBar(RectTransform parent, string label, float y)
            {
                RectTransform row = UiKit.Anchor(UiKit.Rect(parent, label), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(28f, y - 52f), new Vector2(-28f, y));
                TextMeshProUGUI name = UiKit.Label(row, "Label", label, 20f, TextAlignmentOptions.TopLeft, UiKit.TextDim, FontStyles.Bold);
                UiKit.Stretch(name.rectTransform);
                TextMeshProUGUI value = UiKit.Label(row, "Value", "", 20f, TextAlignmentOptions.TopRight, Color.white, FontStyles.Bold);
                UiKit.Stretch(value.rectTransform);
                Image track = UiKit.Image(row, "Track", new Color(0f, 0f, 0f, 0.4f), UiKit.Rounded);
                UiKit.Anchor(track.rectTransform, Vector2.zero, new Vector2(1f, 0f), new Vector2(0f, 4f), new Vector2(0f, 18f));
                Image fill = UiKit.Image(track.transform, "Fill", UiKit.Accent, UiKit.Rounded);
                UiKit.Stretch(fill.rectTransform);
                fill.rectTransform.pivot = new Vector2(0f, 0.5f);
                return (fill.rectTransform, value);
            }

            public void Open(string confirmText, bool required)
            {
                selection[0] = match.SelectedPrimary;
                selection[1] = match.SelectedSecondary;
                selection[2] = match.SelectedMelee;
                confirmLabel.text = confirmText;
                backButton.gameObject.SetActive(!required);
                subtitle.text = $"Team Deathmatch  ·  {match.MapName}  ·  First to {match.ScoreLimit} kills  ·  {Mathf.RoundToInt(match.TimeLimit / 60f)} minutes";
                RefreshCards();
                ShowDetails(Weapon(0, selection[0]));
                root.SetActive(true);
                MenuState.Push(this);
            }

            public void Close()
            {
                if (!root.activeSelf) return;
                root.SetActive(false);
                match.HoldPlayerRespawn = false;
                MenuState.Pop(this);
            }

            void Confirm()
            {
                match.SelectLoadout(selection[0], selection[1], selection[2]);
                root.SetActive(false);
                MenuState.Pop(this);
                // Joins the match, or applies the weapons to the next life and stops holding the respawn.
                confirmed();
            }

            void Select(int slot, int index)
            {
                selection[slot] = index;
                RefreshCards();
                ShowDetails(Weapon(slot, index));
            }

            WeaponDefinition Weapon(int slot, int index)
            {
                IReadOnlyList<WeaponDefinition> list = slot == 0 ? match.Primaries : slot == 1 ? match.Secondaries : match.Melees;
                return list.Count == 0 ? null : list[Mathf.Clamp(index, 0, list.Count - 1)];
            }

            void RefreshCards()
            {
                for (int slot = 0; slot < cards.Length; slot++)
                    for (int i = 0; i < cards[slot].Count; i++)
                        cards[slot][i].color = i == selection[slot] ? UiKit.Selected : UiKit.PanelRaised;
            }

            void ShowDetails(WeaponDefinition weapon)
            {
                if (weapon == null) return;
                detailName.text = weapon.displayName;
                detailCategory.text = weapon.category.ToString().ToUpperInvariant() + (weapon.aimUsesScope ? "  ·  SCOPED" : "");
                float damage = weapon.damage * weapon.pellets;
                float spread = weapon.canAim ? Mathf.Min(weapon.hipSpread, weapon.aimSpread) : weapon.hipSpread;
                SetStat(0, damage / 120f, weapon.pellets > 1 ? $"{weapon.damage:0} x{weapon.pellets}" : $"{damage:0}");
                SetStat(1, weapon.roundsPerMinute / 900f, $"{weapon.roundsPerMinute:0} RPM");
                SetStat(2, weapon.IsMelee ? 1f : 1f - Mathf.Clamp01(spread / 4f), weapon.IsMelee ? "-" : $"{spread:0.00}°");
                SetStat(3, weapon.IsMelee ? weapon.range / 40f : weapon.falloffEnd / 200f, weapon.IsMelee ? $"{weapon.range:0.#} m" : $"{weapon.falloffEnd:0} m");
                SetStat(4, (weapon.moveSpeedFactor - 0.8f) / 0.3f, $"{weapon.moveSpeedFactor * 100f:0}%");
                detailAmmo.text = weapon.IsMelee ? "Melee  ·  no ammo" : $"Magazine {weapon.magazineSize}  ·  Reserve {weapon.reserveAmmo}  ·  Reload {weapon.reloadTime:0.0}s";
            }

            void SetStat(int index, float fraction, string value)
            {
                statBars[index].fill.localScale = new Vector3(Mathf.Clamp(fraction, 0.04f, 1f), 1f, 1f);
                statBars[index].value.text = value;
            }
        }

        // ================================================================== Standings

        /// <summary>Both teams side by side: name, kills, deaths, headshots and score.</summary>
        sealed class StandingsTable
        {
            const int RowsPerTeam = 6;
            static readonly float[] ColumnWidths = { 0f, 70f, 70f, 70f, 110f };

            readonly TdmMatch match;
            readonly TextMeshProUGUI[] headers = new TextMeshProUGUI[2];
            readonly TextMeshProUGUI[,][] rows = new TextMeshProUGUI[2, RowsPerTeam][];
            readonly Image[,] rowBacks = new Image[2, RowsPerTeam];

            public StandingsTable(RectTransform parent, TdmMatch match)
            {
                this.match = match;
                for (int side = 0; side < 2; side++)
                {
                    RectTransform column = UiKit.Anchor(UiKit.Rect(parent, side == 0 ? "Friendly" : "Enemy"),
                        new Vector2(side * 0.5f, 0f), new Vector2(side * 0.5f + 0.5f, 1f), new Vector2(24f, 24f), new Vector2(-24f, -24f));
                    VerticalLayoutGroup layout = column.gameObject.AddComponent<VerticalLayoutGroup>();
                    layout.spacing = 6f;
                    layout.childControlWidth = true;
                    layout.childControlHeight = true;
                    layout.childForceExpandWidth = true;
                    layout.childForceExpandHeight = false;

                    headers[side] = UiKit.Label(column, "Header", "", 34f, TextAlignmentOptions.Left, Color.white, FontStyles.Bold);
                    UiKit.Size(headers[side], 52f);
                    Cells(column, "Columns", new[] { "PLAYER", "K", "D", "HS", "SCORE" }, UiKit.TextDim, 20f);
                    for (int i = 0; i < RowsPerTeam; i++)
                    {
                        Image back = UiKit.Image(column, $"Row{i}", new Color(1f, 1f, 1f, 0.05f), UiKit.Rounded);
                        UiKit.Size(back, 56f);
                        rowBacks[side, i] = back;
                        rows[side, i] = Cells(back.rectTransform, "Cells", new[] { "", "", "", "", "" }, Color.white, 26f, stretch: true);
                    }
                }
            }

            static TextMeshProUGUI[] Cells(RectTransform parent, string name, string[] texts, Color color, float size, bool stretch = false)
            {
                HorizontalLayoutGroup row = UiKit.Row(parent, name, 8f, stretch ? -1f : 32f);
                row.padding = new RectOffset(16, 16, 0, 0);
                if (stretch) UiKit.Stretch((RectTransform)row.transform);
                var cells = new TextMeshProUGUI[texts.Length];
                for (int i = 0; i < texts.Length; i++)
                {
                    cells[i] = UiKit.Label(row.transform, $"Cell{i}", texts[i], size, i == 0 ? TextAlignmentOptions.Left : TextAlignmentOptions.Right, color, i == 0 ? FontStyles.Bold : FontStyles.Normal);
                    LayoutElement element = UiKit.Size(cells[i], 30f, ColumnWidths[i], i == 0 ? 1f : 0f);
                    element.minWidth = i == 0 ? 120f : ColumnWidths[i];
                }
                return cells;
            }

            public void Refresh()
            {
                Team ours = match.PlayerTeam;
                Team[] order = { ours, ours == Team.Attackers ? Team.Defenders : Team.Attackers };
                for (int side = 0; side < 2; side++)
                {
                    Team team = order[side];
                    Color color = TeamColors.For(team);
                    headers[side].text = $"{TeamColors.NameOf(team).ToUpperInvariant()}   {match.Score(team)}";
                    headers[side].color = color;
                    List<MemberStats> standings = match.Standings(team);
                    for (int i = 0; i < RowsPerTeam; i++)
                    {
                        bool used = i < standings.Count;
                        rowBacks[side, i].gameObject.SetActive(used);
                        if (!used) continue;
                        MemberStats stats = standings[i];
                        bool local = stats.Member.IsLocalPlayer;
                        rowBacks[side, i].color = local ? new Color(color.r, color.g, color.b, 0.25f) : new Color(1f, 1f, 1f, stats.Member.IsAlive ? 0.06f : 0.02f);
                        TextMeshProUGUI[] cells = rows[side, i];
                        cells[0].text = stats.Member.DisplayName;
                        cells[0].color = stats.Member.IsAlive ? Color.white : UiKit.TextDim;
                        cells[1].SetText("{0}", stats.Kills);
                        cells[2].SetText("{0}", stats.Deaths);
                        cells[3].SetText("{0}", stats.Headshots);
                        cells[4].SetText("{0}", stats.Score);
                    }
                }
            }
        }
    }
}
