using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AI.Agent;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.AI
{
    public class AIChatWindow : EditorWindow
    {
        // ================================================================
        // CHAT MESSAGE
        // ================================================================

        public class ChatMessage
        {
            public string Role;
            public string Content;
            public DateTime Time;
        }

        // ================================================================
        // CONFIGURATION
        // ================================================================

        // Bu sadece Unity Editor'deki yazma sınırıdır.
        // Diğer hiçbir scriptin ayarını değiştirmez.
        private const int MAX_INPUT_CHARS = 500000;

        private const float MIN_WIDTH = 520f;
        private const float MIN_HEIGHT = 620f;

        private const float HEADER_HEIGHT = 68f;

        private const float MIN_INPUT_HEIGHT = 180f;
        private const float MAX_INPUT_HEIGHT = 280f;

        // ================================================================
        // DATA
        // ================================================================

        private readonly List<ChatMessage> messages =
            new List<ChatMessage>();

        private string inputPrompt = "";

        private Vector2 chatScrollPosition;
        private Vector2 promptScrollPosition;

        // ================================================================
        // STATE
        // ================================================================

        private bool isProcessing = false;
        private bool isInitialized = false;
        private bool initializationFailed = false;

        private string statusMessage =
            "Initializing...";

        private string lastError = "";

        // ================================================================
        // EXISTING AGENT
        // ================================================================

        private OllamaToolAgent agent;

        // ================================================================
        // GUI STYLES
        // ================================================================

        private GUIStyle titleStyle;
        private GUIStyle subtitleStyle;

        private GUIStyle inputStyle;

        private GUIStyle messageHeaderStyle;
        private GUIStyle messageContentStyle;

        private GUIStyle userMessageStyle;
        private GUIStyle assistantMessageStyle;

        private GUIStyle primaryButtonStyle;
        private GUIStyle secondaryButtonStyle;

        private GUIStyle statusStyle;
        private GUIStyle counterStyle;

        private GUIStyle welcomeTitleStyle;
        private GUIStyle welcomeTextStyle;

        private bool stylesInitialized;

        // ================================================================
        // COLORS
        // ================================================================

        private static readonly Color BackgroundColor =
            new Color(0.055f, 0.065f, 0.075f);

        private static readonly Color PanelColor =
            new Color(0.085f, 0.095f, 0.110f);

        private static readonly Color InputColor =
            new Color(0.030f, 0.035f, 0.045f);

        private static readonly Color UserColor =
            new Color(0.055f, 0.120f, 0.165f);

        private static readonly Color AssistantColor =
            new Color(0.095f, 0.100f, 0.115f);

        private static readonly Color AccentColor =
            new Color(0.000f, 0.750f, 0.950f);

        private static readonly Color SuccessColor =
            new Color(0.200f, 0.900f, 0.400f);

        private static readonly Color WarningColor =
            new Color(1.000f, 0.700f, 0.200f);

        private static readonly Color ErrorColor =
            new Color(1.000f, 0.250f, 0.250f);

        private static readonly Color PrimaryText =
            new Color(0.930f, 0.940f, 0.950f);

        private static readonly Color SecondaryText =
            new Color(0.550f, 0.580f, 0.620f);

        // ================================================================
        // MENU
        // ================================================================

        [MenuItem("Tools/AI/MCP Chat Window")]
        public static void ShowWindow()
        {
            AIChatWindow window =
                GetWindow<AIChatWindow>(
                    "AI MCP Chat"
                );

            window.minSize =
                new Vector2(
                    MIN_WIDTH,
                    MIN_HEIGHT
                );

            window.Show();
        }

        // ================================================================
        // ENABLE
        // ================================================================

        private async void OnEnable()
        {
            InitializeStyles();

            isProcessing = false;
            isInitialized = false;
            initializationFailed = false;

            statusMessage =
                "Connecting to AI Agent...";

            Repaint();

            try
            {
                // MEVCUT AGENT
                agent =
                    new OllamaToolAgent();

                // MEVCUT INITIALIZE
                bool result =
                    await agent.Initialize();

                if (result)
                {
                    isInitialized = true;
                    initializationFailed = false;

                    statusMessage =
                        "AI Agent Ready";
                }
                else
                {
                    isInitialized = false;
                    initializationFailed = true;

                    statusMessage =
                        "AI Agent initialization failed.";
                }
            }
            catch (Exception ex)
            {
                isInitialized = false;
                initializationFailed = true;

                lastError = ex.ToString();

                statusMessage =
                    "Initialization Error";

                Debug.LogError(
                    "[AIChatWindow] " +
                    ex
                );
            }

            Repaint();
        }

        // ================================================================
        // DISABLE
        // ================================================================

        private void OnDisable()
        {
            stylesInitialized = false;
        }

        // ================================================================
        // UPDATE
        // ================================================================

        private void Update()
        {
            if (isProcessing)
                Repaint();
        }

        // ================================================================
        // GUI
        // ================================================================

        private void OnGUI()
        {
            InitializeStyles();

            HandleKeyboardShortcuts();

            DrawBackground();

            DrawHeader();

            DrawChatHistory();

            DrawInputArea();
        }

        // ================================================================
        // BACKGROUND
        // ================================================================

        private void DrawBackground()
        {
            EditorGUI.DrawRect(
                new Rect(
                    0,
                    0,
                    position.width,
                    position.height
                ),
                BackgroundColor
            );
        }

        // ================================================================
        // HEADER
        // ================================================================

        private void DrawHeader()
        {
            Rect rect =
                GUILayoutUtility.GetRect(
                    GUIContent.none,
                    GUIStyle.none,
                    GUILayout.Height(
                        HEADER_HEIGHT
                    )
                );

            EditorGUI.DrawRect(
                rect,
                PanelColor
            );

            // Accent line
            EditorGUI.DrawRect(
                new Rect(
                    rect.x,
                    rect.yMax - 2,
                    rect.width,
                    2
                ),
                AccentColor
            );

            GUI.Label(
                new Rect(
                    rect.x + 18,
                    rect.y + 8,
                    rect.width - 250,
                    28
                ),
                "Unity MCP Autonomous Agent",
                titleStyle
            );

            GUI.Label(
                new Rect(
                    rect.x + 18,
                    rect.y + 37,
                    rect.width - 250,
                    20
                ),
                "AI Development & Unity Automation Assistant",
                subtitleStyle
            );

            DrawConnectionStatus(rect);
        }

        // ================================================================
        // CONNECTION STATUS
        // ================================================================

        private void DrawConnectionStatus(
            Rect headerRect
        )
        {
            bool connected = false;

            try
            {
                if (MCPUnityClient.Instance != null)
                {
                    connected =
                        MCPUnityClient.Instance.IsConnected();
                }
            }
            catch
            {
                connected = false;
            }

            string text;
            Color color;

            if (isProcessing)
            {
                text = "●  AI WORKING";
                color = WarningColor;
            }
            else if (isInitialized && connected)
            {
                text = "●  MCP CONNECTED";
                color = SuccessColor;
            }
            else if (isInitialized)
            {
                text = "●  MCP DISCONNECTED";
                color = WarningColor;
            }
            else if (initializationFailed)
            {
                text = "●  AGENT ERROR";
                color = ErrorColor;
            }
            else
            {
                text = "●  INITIALIZING";
                color = SecondaryText;
            }

            GUIStyle style =
                new GUIStyle(
                    EditorStyles.boldLabel
                );

            style.fontSize = 10;
            style.alignment =
                TextAnchor.MiddleRight;

            style.normal.textColor =
                color;

            GUI.Label(
                new Rect(
                    headerRect.xMax - 210,
                    headerRect.y + 23,
                    190,
                    24
                ),
                text,
                style
            );
        }

        // ================================================================
        // CHAT HISTORY
        // ================================================================

        private void DrawChatHistory()
        {
            EditorGUILayout.BeginVertical(
                GUILayout.ExpandHeight(true)
            );

            chatScrollPosition =
                EditorGUILayout.BeginScrollView(
                    chatScrollPosition,
                    GUILayout.ExpandHeight(true)
                );

            if (messages.Count == 0)
            {
                DrawWelcome();
            }
            else
            {
                foreach (ChatMessage message in messages)
                {
                    DrawMessage(message);
                }
            }

            EditorGUILayout.EndScrollView();

            EditorGUILayout.EndVertical();
        }

        // ================================================================
        // WELCOME
        // ================================================================

        private void DrawWelcome()
        {
            GUILayout.Space(50);

            GUILayout.Label(
                "AI MCP Development Assistant",
                welcomeTitleStyle
            );

            GUILayout.Space(12);

            GUILayout.Label(
                "Describe what you want to create, modify, debug or automate inside Unity.",
                welcomeTextStyle
            );

            GUILayout.Space(5);

            GUILayout.Label(
                "Large prompts supported • Ctrl + Enter to send",
                welcomeTextStyle
            );
        }

        // ================================================================
        // MESSAGE
        // ================================================================

        private void DrawMessage(
            ChatMessage message
        )
        {
            if (message == null)
                return;

            bool isUser =
                message.Role == "user";

            EditorGUILayout.BeginVertical(
                isUser
                    ? userMessageStyle
                    : assistantMessageStyle
            );

            EditorGUILayout.BeginHorizontal();

            GUIStyle header =
                new GUIStyle(
                    messageHeaderStyle
                );

            header.normal.textColor =
                isUser
                    ? AccentColor
                    : SuccessColor;

            GUILayout.Label(
                isUser
                    ? "YOU"
                    : "AI AGENT",
                header
            );

            GUILayout.FlexibleSpace();

            if (!isUser)
            {
                if (
                    GUILayout.Button(
                        "COPY",
                        secondaryButtonStyle,
                        GUILayout.Width(55),
                        GUILayout.Height(22)
                    )
                )
                {
                    EditorGUIUtility.systemCopyBuffer =
                        message.Content ?? "";
                }
            }

            GUILayout.Label(
                message.Time.ToString(
                    "HH:mm:ss"
                ),
                statusStyle,
                GUILayout.Width(60)
            );

            EditorGUILayout.EndHorizontal();

            GUILayout.Space(6);

            string content =
                message.Content ?? "";

            float width =
                Mathf.Max(
                    300,
                    position.width - 80
                );

            float height =
                messageContentStyle.CalcHeight(
                    new GUIContent(content),
                    width
                );

            height =
                Mathf.Clamp(
                    height + 12,
                    30,
                    5000
                );

            EditorGUILayout.SelectableLabel(
                content,
                messageContentStyle,
                GUILayout.MinHeight(height),
                GUILayout.ExpandWidth(true)
            );

            EditorGUILayout.EndVertical();

            GUILayout.Space(8);
        }

        // ================================================================
        // INPUT AREA
        // ================================================================

        private void DrawInputArea()
        {
            GUILayout.Space(4);

            EditorGUILayout.BeginVertical(
                CreatePanelStyle()
            );

            // ------------------------------------------------------------
            // PROMPT HEADER
            // ------------------------------------------------------------

            EditorGUILayout.BeginHorizontal();

            GUIStyle promptHeader =
                new GUIStyle(
                    EditorStyles.boldLabel
                );

            promptHeader.fontSize = 11;
            promptHeader.normal.textColor =
                PrimaryText;

            GUILayout.Label(
                "PROMPT",
                promptHeader
            );

            GUILayout.FlexibleSpace();

            UpdateCounterColor();

            GUILayout.Label(
                $"{inputPrompt.Length:N0} / {MAX_INPUT_CHARS:N0}",
                counterStyle
            );

            EditorGUILayout.EndHorizontal();

            GUILayout.Space(6);

            // ------------------------------------------------------------
            // LARGE INPUT
            // ------------------------------------------------------------

            DrawScrollablePrompt();

            GUILayout.Space(8);

            // ------------------------------------------------------------
            // BUTTONS
            // ------------------------------------------------------------

            EditorGUILayout.BeginHorizontal();

            GUI.enabled =
                !isProcessing &&
                inputPrompt.Length > 0;

            if (
                GUILayout.Button(
                    "CLEAR INPUT",
                    secondaryButtonStyle,
                    GUILayout.Width(105),
                    GUILayout.Height(32)
                )
            )
            {
                inputPrompt = "";
                promptScrollPosition =
                    Vector2.zero;

                FocusPrompt();
            }

            GUI.enabled = true;

            if (
                GUILayout.Button(
                    "CLEAR CHAT",
                    secondaryButtonStyle,
                    GUILayout.Width(100),
                    GUILayout.Height(32)
                )
            )
            {
                messages.Clear();

                chatScrollPosition =
                    Vector2.zero;
            }

            GUILayout.FlexibleSpace();

            GUILayout.Label(
                isProcessing
                    ? "AI Agent processing..."
                    : statusMessage,
                statusStyle,
                GUILayout.Width(180)
            );

            bool canSend =
                !isProcessing &&
                isInitialized &&
                !string.IsNullOrWhiteSpace(
                    inputPrompt
                );

            GUI.enabled =
                canSend;

            if (
                GUILayout.Button(
                    "SEND  ▶",
                    primaryButtonStyle,
                    GUILayout.Width(120),
                    GUILayout.Height(34)
                )
            )
            {
                SendCurrentMessage();
            }

            GUI.enabled = true;

            EditorGUILayout.EndHorizontal();

            GUILayout.Space(5);

            GUIStyle hint =
                new GUIStyle(
                    EditorStyles.miniLabel
                );

            hint.normal.textColor =
                new Color(
                    0.42f,
                    0.45f,
                    0.49f
                );

            GUILayout.Label(
                "Enter = New Line   •   Ctrl + Enter = Send   •   Mouse Wheel = Scroll",
                hint
            );

            EditorGUILayout.EndVertical();
        }

        // ================================================================
        // SCROLLABLE PROMPT
        // ================================================================

        private void DrawScrollablePrompt()
        {
            float inputHeight =
                Mathf.Clamp(
                    position.height * 0.23f,
                    MIN_INPUT_HEIGHT,
                    MAX_INPUT_HEIGHT
                );

            Rect outer =
                GUILayoutUtility.GetRect(
                    GUIContent.none,
                    GUIStyle.none,
                    GUILayout.Height(
                        inputHeight
                    ),
                    GUILayout.ExpandWidth(true)
                );

            // Background
            EditorGUI.DrawRect(
                outer,
                InputColor
            );

            // Border
            DrawBorder(
                outer,
                new Color(
                    0.14f,
                    0.16f,
                    0.19f
                )
            );

            Rect scrollRect =
                new Rect(
                    outer.x + 4,
                    outer.y + 4,
                    outer.width - 8,
                    outer.height - 8
                );

            float contentWidth =
                Mathf.Max(
                    100,
                    scrollRect.width - 24
                );

            string text =
                string.IsNullOrEmpty(
                    inputPrompt
                )
                    ? " "
                    : inputPrompt;

            float calculatedHeight =
                inputStyle.CalcHeight(
                    new GUIContent(text),
                    contentWidth - 20
                );

            float contentHeight =
                Mathf.Max(
                    calculatedHeight + 30,
                    scrollRect.height
                );

            // ============================================================
            // SCROLL VIEW
            // ============================================================

            promptScrollPosition =
                GUI.BeginScrollView(
                    scrollRect,
                    promptScrollPosition,
                    new Rect(
                        0,
                        0,
                        contentWidth,
                        contentHeight
                    ),
                    false,
                    true
                );

            // ============================================================
            // TEXT AREA
            // ============================================================

            GUI.SetNextControlName(
                "AI_PROMPT_INPUT"
            );

            Rect textRect =
                new Rect(
                    8,
                    8,
                    contentWidth - 16,
                    contentHeight - 16
                );

            string newText =
                GUI.TextArea(
                    textRect,
                    inputPrompt,
                    MAX_INPUT_CHARS,
                    inputStyle
                );

            if (newText != inputPrompt)
            {
                inputPrompt =
                    newText;

                if (
                    inputPrompt.Length >
                    MAX_INPUT_CHARS
                )
                {
                    inputPrompt =
                        inputPrompt.Substring(
                            0,
                            MAX_INPUT_CHARS
                        );
                }
            }

            GUI.EndScrollView();
        }

        // ================================================================
        // KEYBOARD
        // ================================================================

        private void HandleKeyboardShortcuts()
        {
            Event e =
                Event.current;

            if (e == null)
                return;

            if (
                e.type !=
                EventType.KeyDown
            )
                return;

            bool modifier =
                e.control ||
                e.command;

            // CTRL + ENTER
            if (
                modifier &&
                e.keyCode ==
                KeyCode.Return
            )
            {
                if (
                    !isProcessing &&
                    isInitialized &&
                    !string.IsNullOrWhiteSpace(
                        inputPrompt
                    )
                )
                {
                    SendCurrentMessage();

                    e.Use();
                }
            }

            // CTRL + L
            if (
                modifier &&
                e.keyCode ==
                KeyCode.L
            )
            {
                if (!isProcessing)
                {
                    inputPrompt = "";

                    promptScrollPosition =
                        Vector2.zero;

                    FocusPrompt();

                    e.Use();
                }
            }
        }

        // ================================================================
        // SEND
        // ================================================================

        private async void SendCurrentMessage()
        {
            if (isProcessing)
                return;

            if (!isInitialized)
                return;

            if (
                string.IsNullOrWhiteSpace(
                    inputPrompt
                )
            )
                return;

            string prompt =
                inputPrompt;

            // Sadece UI inputunu temizliyoruz.
            // Agent/Client tarafında hiçbir değişiklik yok.
            inputPrompt = "";

            promptScrollPosition =
                Vector2.zero;

            isProcessing = true;

            statusMessage =
                "Sending request...";

            messages.Add(
                new ChatMessage
                {
                    Role = "user",
                    Content = prompt,
                    Time = DateTime.Now
                }
            );

            ScrollChatBottom();

            Repaint();

            try
            {
                // ========================================================
                // EXISTING MCP CLIENT
                // ========================================================

                if (
                    !MCPUnityClient.Instance.IsConnected()
                )
                {
                    statusMessage =
                        "Connecting to MCP...";

                    Repaint();

                    await
                        MCPUnityClient.Instance.Connect();
                }

                // ========================================================
                // EXISTING AGENT
                // ========================================================

                statusMessage =
                    "Qwen3 Agent working...";

                Repaint();

                string response =
                    await agent.Ask(prompt);

                // ========================================================
                // RETURN TO UNITY EDITOR THREAD
                // ========================================================

                EditorApplication.delayCall += () =>
                {
                    messages.Add(
                        new ChatMessage
                        {
                            Role = "assistant",
                            Content =
                                string.IsNullOrEmpty(response)
                                    ? "AI returned an empty response."
                                    : response,
                            Time = DateTime.Now
                        }
                    );

                    isProcessing = false;

                    statusMessage =
                        "AI Agent Ready";

                    ScrollChatBottom();

                    Repaint();
                };
            }
            catch (Exception ex)
            {
                Debug.LogError(
                    "[AIChatWindow] Request failed:\n" +
                    ex
                );

                EditorApplication.delayCall += () =>
                {
                    lastError =
                        ex.ToString();

                    messages.Add(
                        new ChatMessage
                        {
                            Role = "assistant",
                            Content =
                                "ERROR\n\n" +
                                ex.Message,
                            Time = DateTime.Now
                        }
                    );

                    isProcessing = false;

                    statusMessage =
                        "Request failed";

                    ScrollChatBottom();

                    Repaint();
                };
            }
        }

        // ================================================================
        // CHAT SCROLL
        // ================================================================

        private void ScrollChatBottom()
        {
            EditorApplication.delayCall += () =>
            {
                chatScrollPosition.y =
                    float.MaxValue;

                Repaint();
            };
        }

        // ================================================================
        // FOCUS
        // ================================================================

        private void FocusPrompt()
        {
            EditorApplication.delayCall += () =>
            {
                GUI.FocusControl(
                    "AI_PROMPT_INPUT"
                );

                Repaint();
            };
        }

        // ================================================================
        // COUNTER COLOR
        // ================================================================

        private void UpdateCounterColor()
        {
            if (counterStyle == null)
            {
                counterStyle =
                    new GUIStyle(
                        EditorStyles.miniLabel
                    );
            }

            float ratio =
                (float)inputPrompt.Length /
                MAX_INPUT_CHARS;

            if (ratio >= 0.95f)
                counterStyle.normal.textColor =
                    ErrorColor;
            else if (ratio >= 0.80f)
                counterStyle.normal.textColor =
                    WarningColor;
            else
                counterStyle.normal.textColor =
                    SecondaryText;

            counterStyle.alignment =
                TextAnchor.MiddleRight;
        }

        // ================================================================
        // STYLES
        // ================================================================

        private void InitializeStyles()
        {
            if (stylesInitialized)
                return;

            // ------------------------------------------------------------
            // TITLE
            // ------------------------------------------------------------

            titleStyle =
                new GUIStyle(
                    EditorStyles.boldLabel
                );

            titleStyle.fontSize = 18;
            titleStyle.normal.textColor =
                PrimaryText;

            // ------------------------------------------------------------
            // SUBTITLE
            // ------------------------------------------------------------

            subtitleStyle =
                new GUIStyle(
                    EditorStyles.label
                );

            subtitleStyle.fontSize = 11;
            subtitleStyle.normal.textColor =
                SecondaryText;

            // ------------------------------------------------------------
            // INPUT
            // ------------------------------------------------------------

            inputStyle =
                new GUIStyle(
                    EditorStyles.textArea
                );

            inputStyle.fontSize = 13;
            inputStyle.wordWrap = true;
            inputStyle.richText = false;

            inputStyle.padding =
                new RectOffset(
                    10,
                    10,
                    10,
                    10
                );

            inputStyle.normal.textColor =
                PrimaryText;

            inputStyle.focused.textColor =
                Color.white;

            // ------------------------------------------------------------
            // MESSAGE HEADER
            // ------------------------------------------------------------

            messageHeaderStyle =
                new GUIStyle(
                    EditorStyles.boldLabel
                );

            messageHeaderStyle.fontSize = 11;

            // ------------------------------------------------------------
            // MESSAGE CONTENT
            // ------------------------------------------------------------

            messageContentStyle =
                new GUIStyle(
                    EditorStyles.wordWrappedLabel
                );

            messageContentStyle.fontSize = 12;
            messageContentStyle.wordWrap = true;
            messageContentStyle.richText = true;

            messageContentStyle.normal.textColor =
                PrimaryText;

            messageContentStyle.padding =
                new RectOffset(
                    4,
                    4,
                    2,
                    2
                );

            // ------------------------------------------------------------
            // USER MESSAGE
            // ------------------------------------------------------------

            userMessageStyle =
                new GUIStyle(
                    EditorStyles.helpBox
                );

            userMessageStyle.padding =
                new RectOffset(
                    12,
                    12,
                    10,
                    10
                );

            // ------------------------------------------------------------
            // ASSISTANT MESSAGE
            // ------------------------------------------------------------

            assistantMessageStyle =
                new GUIStyle(
                    EditorStyles.helpBox
                );

            assistantMessageStyle.padding =
                new RectOffset(
                    12,
                    12,
                    10,
                    10
                );

            // ------------------------------------------------------------
            // PRIMARY BUTTON
            // ------------------------------------------------------------

            primaryButtonStyle =
                new GUIStyle(
                    EditorStyles.miniButton
                );

            primaryButtonStyle.fontSize = 11;
            primaryButtonStyle.fontStyle =
                FontStyle.Bold;

            primaryButtonStyle.normal.textColor =
                Color.white;

            // ------------------------------------------------------------
            // SECONDARY BUTTON
            // ------------------------------------------------------------

            secondaryButtonStyle =
                new GUIStyle(
                    EditorStyles.miniButton
                );

            secondaryButtonStyle.fontSize = 10;

            // ------------------------------------------------------------
            // STATUS
            // ------------------------------------------------------------

            statusStyle =
                new GUIStyle(
                    EditorStyles.miniLabel
                );

            statusStyle.fontSize = 10;
            statusStyle.alignment =
                TextAnchor.MiddleRight;

            statusStyle.normal.textColor =
                SecondaryText;

            // ------------------------------------------------------------
            // WELCOME
            // ------------------------------------------------------------

            welcomeTitleStyle =
                new GUIStyle(
                    EditorStyles.boldLabel
                );

            welcomeTitleStyle.fontSize = 22;

            welcomeTitleStyle.alignment =
                TextAnchor.MiddleCenter;

            welcomeTitleStyle.normal.textColor =
                AccentColor;

            welcomeTextStyle =
                new GUIStyle(
                    EditorStyles.wordWrappedLabel
                );

            welcomeTextStyle.fontSize = 12;

            welcomeTextStyle.alignment =
                TextAnchor.MiddleCenter;

            welcomeTextStyle.normal.textColor =
                SecondaryText;

            // ------------------------------------------------------------
            // COUNTER
            // ------------------------------------------------------------

            counterStyle =
                new GUIStyle(
                    EditorStyles.miniLabel
                );

            counterStyle.alignment =
                TextAnchor.MiddleRight;

            counterStyle.normal.textColor =
                SecondaryText;

            stylesInitialized = true;
        }

        // ================================================================
        // PANEL STYLE
        // ================================================================

        private GUIStyle CreatePanelStyle()
        {
            GUIStyle style =
                new GUIStyle(
                    EditorStyles.helpBox
                );

            style.padding =
                new RectOffset(
                    12,
                    12,
                    10,
                    8
                );

            return style;
        }

        // ================================================================
        // BORDER
        // ================================================================

        private void DrawBorder(
            Rect rect,
            Color color
        )
        {
            const float thickness = 1f;

            EditorGUI.DrawRect(
                new Rect(
                    rect.x,
                    rect.y,
                    rect.width,
                    thickness
                ),
                color
            );

            EditorGUI.DrawRect(
                new Rect(
                    rect.x,
                    rect.yMax - thickness,
                    rect.width,
                    thickness
                ),
                color
            );

            EditorGUI.DrawRect(
                new Rect(
                    rect.x,
                    rect.y,
                    thickness,
                    rect.height
                ),
                color
            );

            EditorGUI.DrawRect(
                new Rect(
                    rect.xMax - thickness,
                    rect.y,
                    thickness,
                    rect.height
                ),
                color
            );
        }
    }
}