using BepInEx;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;
using Reptile;
using System.IO;
using System.Text;
using System.Reflection;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using TMPro;

namespace DopeZoneMod
{
    [BepInPlugin("com.desdezerotapes.dopezone", "Dope Zone", "4.4.2")]
    public class Plugin : BaseUnityPlugin
    {
        public static Plugin Instance;

        // Configs
        public ConfigEntry<bool> IsEnabled;
        public ConfigEntry<bool> HideSecondaryNotifications;
        public ConfigEntry<bool> HideHyperBoostEarnNotifs;

        public ConfigEntry<float> ZoneRadius;
        public ConfigEntry<float> TrickGracePeriod;
        public ConfigEntry<int> TricksRequiredForBoost;
        public ConfigEntry<int> TricksToStartZone;
        public ConfigEntry<float> ZoneMoveGracePeriod;
        public ConfigEntry<int> MaxZoneExpansions;
        public ConfigEntry<int> MaxZoneMoves;
        public ConfigEntry<bool> VisibleOzone;

        // State tracking
        private bool _isZoneActive = false;
        private Vector3 _zoneCenter;
        private float _zoneStartTime = 0f;
        private float _lastTrickTime = -100f;
        private float _lastJumpTime = -100f;
        private bool _lastTrickWasDopeJump = false;

        private int _lastTricksInCombo = 0;
        private string _lastTrickType = "";
        private int _currentZoneTrickCount = 0;
        private int _sessionHyperBoosts = 0;

        private int _boostsInCurrentZone = 0;
        private int _zoneExpansionsInCurrentZone = 0;
        private int _zoneMovesInCurrentZone = 0;

        private float _streakCompletedTime = 0f;

        // Movement and expansion
        private bool _isZoneMovingGrace = false;
        private float _zoneMoveGraceEndTime = 0f;
        private bool _pendingExpansionMsg = false;
        private float _expansionMsgStartTime = 0f;

        // Notifications and priority
        private string _priorityMessage = "";
        private float _priorityMessageEndTime = 0f;
        private bool _isPriorityMessageIdleDrop = false;
        private int _currentMessagePriority = 0; // 0 = Idle, 1 = Secondary, 2 = Notifications, 3 = Rewards

        // GUI font handling
        private GameObject _hudCanvasObj;
        private TextMeshProUGUI _hudText;
        private TextMeshProUGUI _popupText;
        private TMP_FontAsset _hudFont;
        private TMP_FontAsset _popupFont;
        private bool _fontSearchAttempted = false;

        // Streak and world visuals
        private GameObject _streakContainer;
        private Image[] _streakIcons = new Image[5];
        private Sprite _streakSprite;

        private GameObject _ozoneVisualObj;
        private Material _ozoneMaterial;
        private float _currentOzoneScale = 0.0001f;

        private Shader _cachedEnvironmentShader = null;

        // DopeDrop tracking
        private bool _wasGroundedOrGrinding = true;
        private bool _isFallingWithoutJump = false;
        private bool _trickedDuringFall = false;
        private bool _dopeDropActive = false;
        private int _dopeDropsInCurrentZone = 0;

        // Audio and strings
        private AudioSource _audioSource;
        private AudioClip _sndHyperBoostEarn;
        private AudioClip _sndDopeZoneLeave;
        private AudioClip _sndDopeDrop;
        private AudioClip _sndStreakEarn;

        private readonly string[] _dopeJumpStrings = {
            "Stylish jump ( >v< )!",
            "Dope jump!",
            "Fly, like a butterfly...",
            "We fly high! 0_0",
            "Peak jump trick!",
            "Aero dynamic!",
            "Power jump! ( /o_o)/",
            "We have Lift-off!",
            "Free Fallin'!",
            "Sky's the limit!",
            "Touch the sky!",
            "High Jump! ^o^",
            "Connecting the zodiacs! *v*",
            "Catch me if you can! (=>v<=)",
            "Above 'em all!",
            "Catching air!"
        };

        private readonly string[] _standaloneDopeDrops = {
            "Sick Gap Drop!",
            "Dope Roll-Off!",
            "Clean Drop!",
            "Heavy Gravity!",
            "Dropdown!",
            "Cool!",
            "Swag!",
            "Sweet!",
            "Heavy Gravity!",
            "Dope Roll-Off!",
            "Street style!",
            "Smooth!",
            "Ledge drop!",
            "Nailed it!",
            "Love that style!",
            "Slam!",
            "Gap potential right here!",
            "Off the curb!",
            "Acid Drop!",
            "Nice one!"
        };

        private readonly string[] _zoneDopeDrops = {
            "You are owning this zone!",
            "Owning this Gap!",
            "That's a nice Gap in the Zone!",
            "Dope Zone Initiated!",
            "Tearing this Gap!",
            "You're on fire! !(^O^)!",
            "Real! (o_O)",
            "Perfect! (^-^)",
            "...they see me rolling!",
            "Lets Freaking Go!",
            "Look at that dopeness!",
            "Shredding this gap!",
            "That's dope! (‹O‹)d",
            "Sweet! (>w<)d",
            "Going All City!",
            "Entering flow state! (>u<)",
            "Rhythm machine! ( -_o)b",
            "Riding dirty d[-_-]b !",
            "I'm Unstoppable!",
            "Street legend! ( >_ <)>",
            "Certified trick classic! ",
            "Dope combo taking place! (O_O)",
            "Another one!",
            "Let's get serious!",
            "Check out that style, baby!",
            "You're now in the DopeZone!",
            "Drop it like its hot!"
        };

        private readonly string[] _idleDopeDrops = {
            "Too simple! (>_<)",
            "Get to tricking!",
            "Where's the flavor?",
            "Basic ahh drop (ToT)",
            "Get movin', kid!",
            "Huh?",
            "got training wheels?",
            "Stale drop... (._.)",
            "Error 404: Styl-o not found (-_-)",
            "Cricket chirp...",
            "Add some spice next time!",
            "You AFK?",
            "Move your feet! (z_z)",
            "Zzz...",
            "Best you got?",
            "Do a falling trick next time!",
            "Ok...",
            "Nothing to see here... (~_~)."
        };

        // File IO
        private string _saveFilePath;

        [Serializable]
        private class TelemetryCache
        {
            public int cache_id;
        }

        private void Awake()
        {
            Instance = this;

            IsEnabled = Config.Bind("1. General", "Enabled", true, "Master switch for the DopeZone mod.");
            HideSecondaryNotifications = Config.Bind("1. General", "Hide Secondary Notifications", false, "Hides Dope Drops notifications (standalones, idles, zone drops), Gap Left Behind, Zone Expanded, and their SFX.");
            HideHyperBoostEarnNotifs = Config.Bind("1. General", "Hide HyperBoost Earn Notifs", false, "Hides HyperBoost earn UI popups (Dope Drop Master, Streak Bonus) and their SFX.");

            ZoneRadius = Config.Bind("2. Zone Settings", "Zone Radius", 70f, "Vector3 units that make up the DopeZone sphere.");
            VisibleOzone = Config.Bind("2. Zone Settings", "Visible Ozone", false, "Show a wireframe 3D representation of the DopeZone.");
            TrickGracePeriod = Config.Bind("2. Zone Settings", "Trick Grace Period", 5.0f, "Seconds allowed between tricks before dropping the zone.");
            ZoneMoveGracePeriod = Config.Bind("2. Zone Settings", "Zone Move Grace Period", 10.0f, "Seconds allowed to move freely to a new area after earning a boost.");
            MaxZoneExpansions = Config.Bind("2. Zone Settings", "Max Zone Expansions", 1, "Max times the zone can expand. 0 = Infinite, -1 = Disabled entirely.");
            MaxZoneMoves = Config.Bind("2. Zone Settings", "Max Zone Moves", 1, "Max times the zone can move. 0 = Infinite, -1 = Disabled entirely.");
            TricksToStartZone = Config.Bind("2. Zone Settings", "Tricks To Start Zone", 3, "Number of tricks required to initiate the DopeZone.");

            TricksRequiredForBoost = Config.Bind("3. Rewards", "Tricks Per HyperBoost", 30, "Number of tricks needed to generate Hyper Boost unit reward.");

            string modFolder = Path.Combine(Paths.PluginPath, "HyperBoostMod");
            if (!Directory.Exists(modFolder))
            {
                Directory.CreateDirectory(modFolder);
            }
            _saveFilePath = Path.Combine(modFolder, "scorecount.json");

            _audioSource = gameObject.AddComponent<AudioSource>();
            _audioSource.spatialBlend = 0f;
            _audioSource.volume = 1f;
            _audioSource.playOnAwake = false;

            StartCoroutine(LoadAudioFiles());
            LoadStreakSprite();
            SetupOzoneVisuals();

            Logger.LogInfo("DopeZone Mod Initialized. Configs mapped, Audio queued, JSON secured. Stay dope.");
        }

        // Applies notification priority so smaller messages do not replace rewards.
        private void SetMessage(string msg, float duration, int priority, bool isIdle = false)
        {
            if (Time.time < _priorityMessageEndTime && _currentMessagePriority > priority)
            {
                return;
            }

            _priorityMessage = msg;
            _priorityMessageEndTime = Time.time + duration;
            _currentMessagePriority = priority;
            _isPriorityMessageIdleDrop = isIdle;
        }

        private void LoadStreakSprite()
        {
            try
            {
                string pluginDir = Path.GetDirectoryName(Info.Location);
                string path = Path.Combine(pluginDir, "Icons", "StreakIcon.png");

                if (File.Exists(path))
                {
                    byte[] bytes = File.ReadAllBytes(path);
                    Texture2D tex = new Texture2D(2, 2);
                    tex.LoadImage(bytes);
                    _streakSprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
                    Logger.LogInfo("Successfully loaded StreakIcon.png for UI");
                }
                else
                {
                    Logger.LogWarning($"StreakIcon.png not found at expected dynamic path: {path}");
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error loading StreakIcon.png: {ex.Message}");
            }
        }

        // Shader handling
        private Shader GetEnvironmentShader()
        {
            if (_cachedEnvironmentShader != null) return _cachedEnvironmentShader;

            // Shader fetch
            _cachedEnvironmentShader = Shader.Find("Sprites/Default");
            if (_cachedEnvironmentShader == null)
            {
                _cachedEnvironmentShader = Shader.Find("Standard");
            }
            return _cachedEnvironmentShader;
        }

        private void SetupOzoneVisuals()
        {
            try
            {
                string pluginDir = Path.GetDirectoryName(Info.Location);
                string ozoneDir = Path.Combine(pluginDir, "OzoneObj");

                if (!Directory.Exists(ozoneDir))
                {
                    Directory.CreateDirectory(ozoneDir);
                }

                string objPath = Path.Combine(ozoneDir, "Ozone.obj");

                if (!File.Exists(objPath))
                {
                    objPath = Path.Combine(pluginDir, "Ozone.obj");
                }

                if (!File.Exists(objPath))
                {
                    Logger.LogWarning($"Ozone.obj not found at expected path: {objPath}");
                    return;
                }

                Mesh ozoneMesh = LoadObjMesh(objPath);
                if (ozoneMesh == null) return;

                _ozoneVisualObj = new GameObject("OzoneVisual");
                MeshFilter mf = _ozoneVisualObj.AddComponent<MeshFilter>();
                mf.mesh = ozoneMesh;

                MeshRenderer mr = _ozoneVisualObj.AddComponent<MeshRenderer>();

                Shader targetShader = GetEnvironmentShader();

                if (targetShader != null)
                {
                    _ozoneMaterial = new Material(targetShader);
                    _ozoneMaterial.renderQueue = 3000;

                    // Keep emission keywords enabled for compatible shader variants.
                    _ozoneMaterial.EnableKeyword("_EMISSION");
                    _ozoneMaterial.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;

                    string texPath = Path.Combine(ozoneDir, "OzoneMat.png");
                    if (File.Exists(texPath))
                    {
                        byte[] texBytes = File.ReadAllBytes(texPath);
                        Texture2D mainTex = new Texture2D(2, 2);
                        mainTex.LoadImage(texBytes);
                        _ozoneMaterial.mainTexture = mainTex;
                    }

                    string emissionTexPath = Path.Combine(ozoneDir, "OzoneMatEmission.png");
                    if (File.Exists(emissionTexPath))
                    {
                        byte[] emBytes = File.ReadAllBytes(emissionTexPath);
                        Texture2D emTex = new Texture2D(2, 2);
                        emTex.LoadImage(emBytes);
                        if (_ozoneMaterial.HasProperty("_EmissionMap"))
                        {
                            _ozoneMaterial.SetTexture("_EmissionMap", emTex);
                        }
                    }

                    // Set the base alpha to 130/255.
                    Color initialColor = Color.HSVToRGB(0f, 1f, 1f);
                    initialColor.a = 130f / 255f;

                    // Raise RGB values to enable emission while preserving alpha.
                    Color emissiveColor = new Color(initialColor.r * 3f, initialColor.g * 3f, initialColor.b * 3f, initialColor.a);
                    _ozoneMaterial.color = emissiveColor;

                    if (_ozoneMaterial.HasProperty("_EmissionColor"))
                    {
                        _ozoneMaterial.SetColor("_EmissionColor", initialColor * 3.0f);
                    }

                    mr.material = _ozoneMaterial;
                }
                else
                {
                    Logger.LogWarning("Target shader not found. Ensure game assets are loaded.");
                }

                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;

                _ozoneVisualObj.SetActive(false);
                Logger.LogInfo("Successfully loaded and initialized Ozone.obj visual with Material enhancements.");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error loading Ozone.obj: {ex.Message}");
            }
        }

        private Mesh LoadObjMesh(string path)
        {
            List<Vector3> vertices = new List<Vector3>();
            List<int> triangles = new List<int>();

            string[] lines = File.ReadAllLines(path);
            foreach (string line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                string[] parts = line.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

                if (parts[0] == "v")
                {
                    float x = -float.Parse(parts[1], CultureInfo.InvariantCulture);
                    float y = float.Parse(parts[2], CultureInfo.InvariantCulture);
                    float z = float.Parse(parts[3], CultureInfo.InvariantCulture);
                    vertices.Add(new Vector3(x, y, z));
                }
                else if (parts[0] == "f")
                {
                    for (int i = 1; i <= 3; i++)
                    {
                        string[] vertexData = parts[i].Split('/');
                        int vIndex = int.Parse(vertexData[0]) - 1;
                        triangles.Add(vIndex);
                    }

                    if (parts.Length > 4)
                    {
                        string[] v1Data = parts[1].Split('/');
                        string[] v3Data = parts[3].Split('/');
                        string[] v4Data = parts[4].Split('/');

                        triangles.Add(int.Parse(v1Data[0]) - 1);
                        triangles.Add(int.Parse(v3Data[0]) - 1);
                        triangles.Add(int.Parse(v4Data[0]) - 1);
                    }
                }
            }

            Mesh mesh = new Mesh();
            mesh.name = "default";
            mesh.vertices = vertices.ToArray();

            for (int i = 0; i < triangles.Count; i += 3)
            {
                int temp = triangles[i];
                triangles[i] = triangles[i + 2];
                triangles[i + 2] = temp;
            }

            mesh.triangles = triangles.ToArray();
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            return mesh;
        }

        private void Update()
        {
            if (!IsEnabled.Value)
            {
                if (_ozoneVisualObj != null && _ozoneVisualObj.activeSelf)
                    _ozoneVisualObj.SetActive(false);
                return;
            }

            var player = WorldHandler.instance?.GetCurrentPlayer();
            if (player == null || player.motor == null) return;

            ProcessDopeZoneLogic(player);
            UpdateCustomHUD(player);
            UpdateOzoneVisual();
        }

        private void UpdateOzoneVisual()
        {
            if (_ozoneVisualObj == null) return;

            if (_isZoneActive && VisibleOzone.Value)
            {
                if (!_ozoneVisualObj.activeSelf) _ozoneVisualObj.SetActive(true);

                if (_ozoneMaterial != null)
                {
                    Shader targetShader = GetEnvironmentShader();
                    if (targetShader != null && _ozoneMaterial.shader != targetShader)
                    {
                        _ozoneMaterial.shader = targetShader;
                        _ozoneMaterial.renderQueue = 3000;
                    }
                }

                _ozoneVisualObj.transform.position = _zoneCenter;

                float targetMaxRadius = ZoneRadius.Value + (ZoneRadius.Value * _zoneExpansionsInCurrentZone);
                _currentOzoneScale = Mathf.Lerp(_currentOzoneScale, targetMaxRadius, Time.deltaTime * 2f);
                _ozoneVisualObj.transform.localScale = new Vector3(_currentOzoneScale, _currentOzoneScale, _currentOzoneScale);
                _ozoneVisualObj.transform.Rotate(Vector3.up, 10f * Time.deltaTime, Space.World);

                if (_ozoneMaterial != null)
                {
                    Color rainbowColor = Color.HSVToRGB(Mathf.Repeat(Time.time * 0.10f, 1f), 1f, 1f);
                    rainbowColor.a = 130f / 255f;

                    // Apply HDR emission color.
                    Color emissiveRainbow = new Color(rainbowColor.r * 3f, rainbowColor.g * 3f, rainbowColor.b * 3f, rainbowColor.a);
                    _ozoneMaterial.color = emissiveRainbow;

                    if (_ozoneMaterial.HasProperty("_EmissionColor"))
                    {
                        _ozoneMaterial.SetColor("_EmissionColor", rainbowColor * 3.0f);
                    }
                }
            }
            else
            {
                if (_ozoneVisualObj.activeSelf) _ozoneVisualObj.SetActive(false);
            }
        }

        private void ProcessDopeZoneLogic(Player player)
        {
            string pendingSecondaryMsg = null;
            int pendingSecondaryMsgPriority = 0;
            bool isNewMsgIdle = false;

            bool isComboing = false;
            try { isComboing = player.IsComboing(); } catch { }

            bool isJumping = false;
            try { isJumping = (bool)(GetFieldValue(player, "isJumping") ?? false); } catch { }

            bool jumpedThisFrame = false;
            try { jumpedThisFrame = (bool)(GetFieldValue(player, "jumpedThisFrame") ?? false); } catch { }

            bool isGrounded = player.motor.isGrounded;
            bool isGrinding = false;
            try { isGrinding = player.IsGrinding(); } catch { }

            bool isGroundedOrGrinding = isGrounded || isGrinding;

            // Jump timing and Dope Jump detection.
            // Track jump events independently of grounded state.
            if (jumpedThisFrame || (isJumping && Time.time - _lastJumpTime > 0.5f))
            {
                _lastJumpTime = Time.time;

                float timeSinceTrick = Time.time - _lastTrickTime;

                // Account for tricks registered just before the jump event.
                // Ignore transitions into a grind to prevent false Dope Jump detection.
                if (!isGrinding && timeSinceTrick >= 0f && timeSinceTrick <= 0.25f && !_lastTrickWasDopeJump)
                {
                    _currentZoneTrickCount += 3; // Retroactive Dope Jump bonus.
                    _lastTrickWasDopeJump = true;

                    if (!HideSecondaryNotifications.Value)
                    {
                        string msg = _dopeJumpStrings[UnityEngine.Random.Range(0, _dopeJumpStrings.Length)];
                        pendingSecondaryMsg = $"<color=#F2CA3B>{msg}</color>";
                        pendingSecondaryMsgPriority = 1;
                    }

                    if (!_isZoneActive && _currentZoneTrickCount >= TricksToStartZone.Value)
                    {
                        _isZoneActive = true;
                        _currentOzoneScale = 0.0001f;
                        _zoneCenter = player.transform.position;
                        _zoneStartTime = Time.time;
                        _boostsInCurrentZone = 0;
                        _zoneExpansionsInCurrentZone = 0;
                        _zoneMovesInCurrentZone = 0;
                        _dopeDropsInCurrentZone = 0;
                    }
                }
            }

            // Fall detection
            if (_wasGroundedOrGrinding && !isGroundedOrGrinding)
            {
                if (!isJumping && !jumpedThisFrame && (Time.time - _lastJumpTime > 0.2f))
                {
                    _isFallingWithoutJump = true;
                    if (Time.time - _lastTrickTime <= 0.85f)
                    {
                        _trickedDuringFall = true;
                    }
                    else
                    {
                        _trickedDuringFall = false;
                    }
                }
            }

            // Fall trick resolution
            if (_isFallingWithoutJump && isGroundedOrGrinding)
            {
                if (_trickedDuringFall)
                {
                    _dopeDropActive = true;
                    if (_isZoneActive)
                    {
                        _dopeDropsInCurrentZone++;
                    }

                    if (!HideSecondaryNotifications.Value)
                    {
                        string[] pool = _isZoneActive ? _zoneDopeDrops : _standaloneDopeDrops;
                        string msg = pool[UnityEngine.Random.Range(0, pool.Length)];
                        pendingSecondaryMsg = $"<color=#00FFFF>{msg}</color>";
                        pendingSecondaryMsgPriority = 1;

                        if (_sndDopeDrop != null) _audioSource.PlayOneShot(_sndDopeDrop);
                    }
                }
                else
                {
                    bool currentlyShowingIdle = _isPriorityMessageIdleDrop && Time.time < _priorityMessageEndTime;
                    if (!currentlyShowingIdle && !HideSecondaryNotifications.Value)
                    {
                        string msg = _idleDopeDrops[UnityEngine.Random.Range(0, _idleDopeDrops.Length)];
                        pendingSecondaryMsg = $"<color=#A9A9A9>{msg}</color>";
                        pendingSecondaryMsgPriority = 0;
                        isNewMsgIdle = true;
                    }
                }

                _isFallingWithoutJump = false;
                _trickedDuringFall = false;
            }
            _wasGroundedOrGrinding = isGroundedOrGrinding;

            int currentTricks = GetTricksInCombo(player);
            string trickTypeString = GetFieldValue(player, "currentTrickType")?.ToString() ?? "";

            bool newComboTrick = currentTricks > _lastTricksInCombo;
            bool newVertTrick = false;

            if (trickTypeString != _lastTrickType && !string.IsNullOrEmpty(trickTypeString))
            {
                string tLower = trickTypeString.ToLower();
                if (tLower.Contains("vert") || tLower.Contains("checkvert") ||
                    tLower.Contains("vertenter") || tLower.Contains("vertexit") ||
                    tLower.Contains("airvert"))
                {
                    newVertTrick = true;
                }
            }
            _lastTrickType = trickTypeString;

            // Air tricks and jumps
            if (newComboTrick || newVertTrick)
            {
                _lastTrickTime = Time.time;
                _lastTrickWasDopeJump = false;

                if (_isFallingWithoutJump)
                {
                    _trickedDuringFall = true;
                }

                int trickIncrement = 1;
                if (trickTypeString.Contains("BOOST"))
                {
                    trickIncrement = 2;
                }

                float timeSinceJump = Time.time - _lastJumpTime;

                // Ignore air tricks while grinding to avoid false positives on rail auto-snap.
                if (!isGrinding)
                {
                    // Allow Dope Jumps before the grounded state updates.
                    // Trick events can arrive before the engine clears the grounded collider state.
                    if (timeSinceJump >= 0f && timeSinceJump <= 0.25f)
                    {
                        trickIncrement = 4;
                        _lastTrickWasDopeJump = true;

                        if (!HideSecondaryNotifications.Value)
                        {
                            string msg = _dopeJumpStrings[UnityEngine.Random.Range(0, _dopeJumpStrings.Length)];
                            pendingSecondaryMsg = $"<color=#F2CA3B>{msg}</color>";
                            pendingSecondaryMsgPriority = 1;
                        }
                    }
                    else if (!isGrounded && timeSinceJump <= 0.5f)
                    {
                        // Legacy Super Jump check.
                        trickIncrement = 3;
                    }
                }

                _currentZoneTrickCount += trickIncrement;

                if (!_isZoneActive && _currentZoneTrickCount >= TricksToStartZone.Value)
                {
                    _isZoneActive = true;
                    _currentOzoneScale = 0.0001f;
                    _zoneCenter = player.transform.position;
                    _zoneStartTime = Time.time;
                    _boostsInCurrentZone = 0;
                    _zoneExpansionsInCurrentZone = 0;
                    _zoneMovesInCurrentZone = 0;
                    _dopeDropsInCurrentZone = 0;
                }
            }

            if (isComboing)
            {
                _lastTricksInCombo = currentTricks;
            }
            else
            {
                _lastTricksInCombo = 0;
            }

            bool isMovingGraceActive = _isZoneActive && _isZoneMovingGrace;
            if (!isMovingGraceActive && (_isZoneActive || _currentZoneTrickCount > 0) && (Time.time - _lastTrickTime > TrickGracePeriod.Value))
            {
                BankHyperBoosts();
                EndZone(null, pendingSecondaryMsg);
                return;
            }

            if (_isZoneActive)
            {
                if (_isZoneMovingGrace)
                {
                    if (Time.time >= _zoneMoveGraceEndTime)
                    {
                        _isZoneMovingGrace = false;
                        _zoneCenter = player.transform.position;
                        _currentOzoneScale = 0.0001f;
                        _zoneStartTime = Time.time;
                        _lastTrickTime = Time.time;
                    }
                }
                else
                {
                    float distanceFromCenter = Vector3.Distance(player.transform.position, _zoneCenter);
                    float currentMaxRadius = ZoneRadius.Value + (ZoneRadius.Value * _zoneExpansionsInCurrentZone);

                    if (distanceFromCenter > currentMaxRadius)
                    {
                        EndZone("<color=#FFA500>DopeZone Notification:</color> <color=#FFFFFF>Gap left behind... (x_x)</color>", pendingSecondaryMsg);
                        return;
                    }
                }

                if (_currentZoneTrickCount >= TricksRequiredForBoost.Value)
                {
                    GrantHyperBoost(pendingSecondaryMsg);
                    pendingSecondaryMsg = null;
                }
            }

            // Expansion notification
            if (_pendingExpansionMsg && Time.time >= _expansionMsgStartTime)
            {
                if (!HideSecondaryNotifications.Value)
                {
                    SetMessage("<color=#00FFFF>Zone Expanded!</color>", 1.75f, 2, false);
                }
                _pendingExpansionMsg = false;
            }

            // Secondary notification
            if (!string.IsNullOrEmpty(pendingSecondaryMsg))
            {
                SetMessage(pendingSecondaryMsg, 3f, pendingSecondaryMsgPriority, isNewMsgIdle);
            }
        }

        private void SetupCustomHUD()
        {
            if (_hudCanvasObj != null) return;

            _fontSearchAttempted = false;
            _hudFont = null;
            _popupFont = null;

            _hudCanvasObj = new GameObject("DopeZoneHUDCanvas");
            Canvas canvas = _hudCanvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 9999;

            _hudCanvasObj.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;

            GameObject textObj = new GameObject("Text");
            textObj.transform.SetParent(_hudCanvasObj.transform, false);
            _hudText = textObj.AddComponent<TextMeshProUGUI>();

            _hudText.alignment = TextAlignmentOptions.TopRight;
            RectTransform rt = textObj.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1, 1);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(1, 1);
            rt.anchoredPosition = new Vector2(-40, -40);
            rt.sizeDelta = new Vector2(600, 200);

            _hudText.fontSize = 24;
            _hudText.richText = true;
            _hudText.raycastTarget = false;
            _hudText.gameObject.SetActive(false);

            GameObject popupObj = new GameObject("PopupText");
            popupObj.transform.SetParent(_hudCanvasObj.transform, false);
            _popupText = popupObj.AddComponent<TextMeshProUGUI>();

            _popupText.alignment = TextAlignmentOptions.Center;
            RectTransform prt = popupObj.GetComponent<RectTransform>();
            prt.anchorMin = new Vector2(0.5f, 0.25f);
            prt.anchorMax = new Vector2(0.5f, 0.25f);
            prt.pivot = new Vector2(0.5f, 0.5f);
            prt.anchoredPosition = Vector2.zero;
            prt.sizeDelta = new Vector2(1000, 250);

            _popupText.fontSize = 26;
            _popupText.richText = true;
            _popupText.color = Color.white;
            _popupText.overflowMode = TextOverflowModes.Overflow;
            _popupText.raycastTarget = false;
            _popupText.gameObject.SetActive(false);

            _streakContainer = new GameObject("StreakContainer");
            _streakContainer.transform.SetParent(_hudCanvasObj.transform, false);
            RectTransform srt = _streakContainer.AddComponent<RectTransform>();
            srt.anchorMin = new Vector2(1, 1);
            srt.anchorMax = new Vector2(1, 1);
            srt.pivot = new Vector2(1, 1);

            srt.sizeDelta = new Vector2(165, 40);
            srt.anchoredPosition = new Vector2(-40, -95);

            for (int i = 0; i < 5; i++)
            {
                GameObject iconObj = new GameObject($"StreakIcon_{i}");
                iconObj.transform.SetParent(_streakContainer.transform, false);
                RectTransform irt = iconObj.AddComponent<RectTransform>();

                irt.anchorMin = new Vector2(0, 0.5f);
                irt.anchorMax = new Vector2(0, 0.5f);
                irt.pivot = new Vector2(0, 0.5f);

                irt.sizeDelta = new Vector2(33, 40);
                irt.anchoredPosition = new Vector2(i * 33, 0);

                Image img = iconObj.AddComponent<Image>();
                img.raycastTarget = false;
                if (_streakSprite != null)
                {
                    img.sprite = _streakSprite;
                }
                _streakIcons[i] = img;
            }
            _streakContainer.SetActive(false);

            _hudCanvasObj.SetActive(false);
        }

        private void HijackGameFont()
        {
            if (_fontSearchAttempted && _hudFont != null && _popupFont != null) return;

            var notifications = Resources.FindObjectsOfTypeAll<GameNotificationUI>();
            if (notifications != null && notifications.Length > 0)
            {
                foreach (var notif in notifications)
                {
                    if (notif != null && notif.messageText != null && notif.messageText.font != null)
                    {
                        _hudFont = notif.messageText.font;
                        break;
                    }
                }
            }

            var allTMPs = Resources.FindObjectsOfTypeAll<TextMeshProUGUI>();
            if (allTMPs != null && allTMPs.Length > 0)
            {
                foreach (var tmp in allTMPs)
                {
                    if (tmp == null || tmp.font == null) continue;
                    string fontName = tmp.font.name.ToLower();

                    if (_hudFont == null && (fontName.Contains("vcr") || fontName.Contains("sora") || fontName.Contains("orbitron")))
                    {
                        _hudFont = tmp.font;
                    }

                    if (_popupFont == null && (fontName.Contains("edo") || fontName.Contains("tungsten") || fontName.Contains("bangers") || fontName.Contains("bebas")))
                    {
                        if (_hudFont == null || tmp.font.name != _hudFont.name)
                        {
                            _popupFont = tmp.font;
                        }
                    }
                }
            }

            if (_hudFont == null || _popupFont == null)
            {
                var fontAssets = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
                if (fontAssets != null && fontAssets.Length > 0)
                {
                    foreach (var font in fontAssets)
                    {
                        if (font == null) continue;
                        string fName = font.name.ToLower();

                        if (_hudFont == null) _hudFont = font;

                        if (_popupFont == null && (fName.Contains("edo") || fName.Contains("tungsten") || fName.Contains("bebas") || fName.Contains("square")))
                        {
                            if (_hudFont == null || font.name != _hudFont.name)
                            {
                                _popupFont = font;
                            }
                        }
                    }
                }
            }

            if (_popupFont == null) _popupFont = _hudFont;
            if (_hudFont == null) _hudFont = _popupFont;

            if (_hudFont != null && _hudText != null) ApplyFontToText(_hudText, _hudFont);
            if (_popupFont != null && _popupText != null) ApplyFontToText(_popupText, _popupFont);

            if (_hudFont != null && _popupFont != null)
            {
                _fontSearchAttempted = true;
            }
        }

        private void ApplyFontToText(TextMeshProUGUI tmpText, TMP_FontAsset font)
        {
            if (tmpText == null || font == null) return;

            tmpText.font = font;

            if (font.material != null)
            {
                Material matInstance = new Material(font.material);
                if (matInstance.HasProperty("_UnderlayColor"))
                {
                    matInstance.EnableKeyword("UNDERLAY_ON");
                    matInstance.SetColor("_UnderlayColor", new Color(0f, 0f, 0f, 0.4f));
                    matInstance.SetFloat("_UnderlayOffsetY", -0.75f);
                    matInstance.SetFloat("_UnderlayOffsetX", 0.75f);
                    matInstance.SetFloat("_UnderlayDilate", 0.15f);
                    matInstance.SetFloat("_UnderlaySoftness", 0.15f);
                }
                tmpText.fontMaterial = matInstance;
            }
        }

        private void UpdateCustomHUD(Player player)
        {
            if (Time.timeScale <= 0) return;

            bool isShowingPriorityMessage = Time.time < _priorityMessageEndTime && !string.IsNullOrEmpty(_priorityMessage);
            bool isTrackingZone = _isZoneActive || _currentZoneTrickCount > 0;

            if (isTrackingZone || isShowingPriorityMessage)
            {
                if (_hudCanvasObj == null) SetupCustomHUD();
                if (!_fontSearchAttempted || _hudFont == null || _popupFont == null) HijackGameFont();
                if (!_hudCanvasObj.activeSelf) _hudCanvasObj.SetActive(true);

                if (isTrackingZone)
                {
                    if (!_hudText.gameObject.activeSelf) _hudText.gameObject.SetActive(true);

                    string countColor = _isZoneActive ? "#39FF14" : "#FFFFFF";
                    string requiredColor = "#00FFFF";
                    string slashColor = "#A9A9A9";

                    string tricksText = $"<color={countColor}>{_currentZoneTrickCount}</color> <color={slashColor}>/</color> <color={requiredColor}>{TricksRequiredForBoost.Value}</color>";

                    float currentMaxRadius = ZoneRadius.Value + (ZoneRadius.Value * _zoneExpansionsInCurrentZone);
                    float currentDistance = _isZoneActive ? Vector3.Distance(player.transform.position, _zoneCenter) : 0.0f;

                    string distText = "";

                    if (_isZoneMovingGrace)
                    {
                        float timeRemaining = _zoneMoveGraceEndTime - Time.time;
                        distText = $"<color=#00FFFF>MOVING {timeRemaining:F1}s</color>";
                    }
                    else
                    {
                        string distColor = (currentDistance > currentMaxRadius * 0.85f) ? "#FFA500" : "#FFFFFF";
                        distText = $"<color={distColor}>{currentDistance:F2}</color> <color={slashColor}>/</color> {currentMaxRadius:F2}";
                    }

                    _hudText.text = $"{tricksText}\n{distText}";

                    int currentStreakLevel = _boostsInCurrentZone % 5;
                    bool isFullStreak = (_boostsInCurrentZone > 0 && currentStreakLevel == 0);

                    bool showStreak = false;
                    if (_boostsInCurrentZone > 0)
                    {
                        if (isFullStreak)
                        {
                            if (Time.time < _streakCompletedTime + 5f)
                            {
                                showStreak = true;
                            }
                        }
                        else
                        {
                            showStreak = true;
                        }
                    }

                    if (showStreak)
                    {
                        if (!_streakContainer.activeSelf) _streakContainer.SetActive(true);

                        float jitterX = (Mathf.PerlinNoise(Time.time * 25f, 0f) - 0.5f) * 7f;
                        float jitterY = (Mathf.PerlinNoise(0f, Time.time * 25f) - 0.5f) * 7f;

                        _streakContainer.GetComponent<RectTransform>().anchoredPosition = new Vector2(-40, -95) + new Vector2(jitterX, jitterY);

                        int visualStreak = isFullStreak ? 5 : currentStreakLevel;
                        bool doRainbow = isFullStreak;
                        bool doNeonGreen = (visualStreak == 4 && !isFullStreak);

                        for (int i = 0; i < 5; i++)
                        {
                            bool isUngrayed = i < visualStreak;
                            if (isUngrayed)
                            {
                                if (doRainbow)
                                {
                                    _streakIcons[i].color = Color.HSVToRGB(Mathf.Repeat(Time.time * 2.5f + (i * 0.1f), 1f), 1f, 1f);
                                }
                                else if (doNeonGreen)
                                {
                                    Color neonGreen = new Color(0.22f, 1.0f, 0.08f);
                                    float pulseLerp = (Mathf.Sin(Time.time * 4f) + 1f) / 2f;
                                    _streakIcons[i].color = Color.Lerp(Color.white, neonGreen, pulseLerp);
                                }
                                else
                                {
                                    _streakIcons[i].color = Color.white;
                                }
                            }
                            else
                            {
                                _streakIcons[i].color = new Color(0.08f, 0.08f, 0.08f, 0.6f);
                            }
                        }
                    }
                    else
                    {
                        if (_streakContainer != null && _streakContainer.activeSelf) _streakContainer.SetActive(false);
                    }
                }
                else
                {
                    if (_hudText.gameObject.activeSelf) _hudText.gameObject.SetActive(false);
                    if (_streakContainer != null && _streakContainer.activeSelf) _streakContainer.SetActive(false);
                }

                if (isShowingPriorityMessage)
                {
                    if (!_popupText.gameObject.activeSelf) _popupText.gameObject.SetActive(true);
                    _popupText.text = "\n" + _priorityMessage;
                }
                else
                {
                    if (_popupText.gameObject.activeSelf) _popupText.gameObject.SetActive(false);
                }
            }
            else
            {
                if (_hudCanvasObj != null && _hudCanvasObj.activeSelf)
                {
                    _hudCanvasObj.SetActive(false);
                }
            }
        }

        private IEnumerator LoadAudioFiles()
        {
            string folder = Path.GetDirectoryName(Info.Location);
            string soundsFolder = Path.Combine(folder, "Sounds");

            yield return StartCoroutine(LoadClip(ResolveAudioPath(folder, soundsFolder, "hyperboost_earn.wav"), clip => _sndHyperBoostEarn = clip));
            yield return StartCoroutine(LoadClip(ResolveAudioPath(folder, soundsFolder, "dopezone_notification.wav"), clip => _sndDopeZoneLeave = clip));
            yield return StartCoroutine(LoadClip(ResolveAudioPath(folder, soundsFolder, "dopezone_notification_long.wav"), clip => _sndDopeDrop = clip));

            string streakPath = ResolveAudioPath(folder, soundsFolder, "streak_sound.wav");
            yield return StartCoroutine(LoadClip(streakPath, clip => _sndStreakEarn = clip));
        }

        private string ResolveAudioPath(string folder, string soundsFolder, string fileName)
        {
            string subfolderPath = Path.Combine(soundsFolder, fileName);
            if (File.Exists(subfolderPath)) return subfolderPath;

            string rootPath = Path.Combine(folder, fileName);
            if (File.Exists(rootPath)) return rootPath;

            return subfolderPath;
        }

        private IEnumerator LoadClip(string path, Action<AudioClip> callback)
        {
            if (!File.Exists(path))
            {
                Logger.LogWarning($"Audio file missing at: {path}");
                yield break;
            }

            string uriPath = new Uri(path).AbsoluteUri;
            using (UnityWebRequest uwr = UnityWebRequestMultimedia.GetAudioClip(uriPath, AudioType.WAV))
            {
                yield return uwr.SendWebRequest();
                if (uwr.result == UnityWebRequest.Result.Success)
                {
                    AudioClip clip = DownloadHandlerAudioClip.GetContent(uwr);
                    if (clip != null)
                    {
                        callback(clip);
                        Logger.LogInfo($"Successfully loaded audio clip: {Path.GetFileName(path)}");
                    }
                }
                else
                {
                    Logger.LogError($"Failed to load audio {Path.GetFileName(path)}: {uwr.error}");
                }
            }
        }

        private int GetTricksInCombo(Player player)
        {
            object val = GetFieldValue(player, "tricksInCombo");
            if (val != null)
            {
                try { return Convert.ToInt32(val); } catch { }
            }
            return 0;
        }

        private object GetFieldValue(object obj, string name)
        {
            if (obj == null) return null;
            var field = obj.GetType().GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            return field?.GetValue(obj);
        }

        private void GrantHyperBoost(string topMessage = null)
        {
            int multiplier = 1;
            int dopeDropThreshold = Mathf.Max(1, TricksRequiredForBoost.Value / 4);

            if (_dopeDropsInCurrentZone >= dopeDropThreshold)
            {
                multiplier = 3;
                string dropMasterTxt = "<color=#FF87EE>DOPE DROP MASTER! x3 MULTIPLIER!</color>";
                topMessage = !string.IsNullOrEmpty(topMessage) ? $"{dropMasterTxt}\n{topMessage}" : dropMasterTxt;
            }
            else if (_dopeDropActive)
            {
                multiplier = 2;
            }

            _boostsInCurrentZone++;

            int completions = _boostsInCurrentZone;
            int streakBonus = 0;
            bool isFiveStreak = (completions > 0 && completions % 5 == 0);

            if (isFiveStreak)
            {
                // Flat 50-unit bonus for every fifth boost.
                streakBonus = 50;
                string streakTxt = $"<color=#FFD700>STREAK x{completions}! +{streakBonus} BONUS HYPERBOOST(S)!</color>";
                topMessage = !string.IsNullOrEmpty(topMessage) ? $"{streakTxt}\n{topMessage}" : streakTxt;

                _streakCompletedTime = Time.time;
            }

            int boostsEarned = (10 * multiplier) + streakBonus;

            _sessionHyperBoosts += boostsEarned;
            _currentZoneTrickCount = 0;
            _dopeDropActive = false;
            _dopeDropsInCurrentZone = 0;

            bool canMove = MaxZoneMoves.Value == 0 || (MaxZoneMoves.Value > 0 && _zoneMovesInCurrentZone < MaxZoneMoves.Value);
            bool canExpand = MaxZoneExpansions.Value == 0 || (MaxZoneExpansions.Value > 0 && _zoneExpansionsInCurrentZone < MaxZoneExpansions.Value);

            string mainText = $"<color=#39FF14>GAP OWNED! {boostsEarned} Hyper Boost Units earned!</color>";
            string combinedText = !string.IsNullOrEmpty(topMessage) ? $"{topMessage}\n{mainText}" : mainText;

            if (!HideHyperBoostEarnNotifs.Value)
            {
                SetMessage(combinedText, 3.5f, 3, false);

                if (isFiveStreak)
                {
                    if (_sndStreakEarn != null) _audioSource.PlayOneShot(_sndStreakEarn);
                    else if (_sndHyperBoostEarn != null) _audioSource.PlayOneShot(_sndHyperBoostEarn);
                }
                else
                {
                    if (_sndHyperBoostEarn != null) _audioSource.PlayOneShot(_sndHyperBoostEarn);
                }
            }

            if (canMove)
            {
                _isZoneMovingGrace = true;
                _zoneMoveGraceEndTime = Time.time + ZoneMoveGracePeriod.Value;
                _zoneMovesInCurrentZone++;
            }

            if (canExpand)
            {
                _pendingExpansionMsg = true;
                _expansionMsgStartTime = HideHyperBoostEarnNotifs.Value ? Time.time : _priorityMessageEndTime;
                _zoneExpansionsInCurrentZone++;
            }

            BankHyperBoosts();
        }

        private void EndZone(string breakMessage, string topMessage = null)
        {
            _isZoneActive = false;
            _currentZoneTrickCount = 0;
            _dopeDropActive = false;

            _dopeDropsInCurrentZone = 0;
            _boostsInCurrentZone = 0;
            _zoneExpansionsInCurrentZone = 0;
            _zoneMovesInCurrentZone = 0;

            _isZoneMovingGrace = false;
            _pendingExpansionMsg = false;

            if (HideSecondaryNotifications.Value)
            {
                if (breakMessage != null && breakMessage.Contains("Gap left behind")) breakMessage = null;
                topMessage = null;
            }

            string fullMessage = "";
            if (!string.IsNullOrEmpty(topMessage)) fullMessage += topMessage + "\n";
            if (!string.IsNullOrEmpty(breakMessage)) fullMessage += breakMessage;

            if (!string.IsNullOrEmpty(fullMessage))
            {
                SetMessage(fullMessage, 3.5f, 2, false);
            }

            if (!string.IsNullOrEmpty(breakMessage) && !HideSecondaryNotifications.Value && _sndDopeZoneLeave != null)
            {
                _audioSource.PlayOneShot(_sndDopeZoneLeave);
            }
        }

        private void BankHyperBoosts()
        {
            if (_sessionHyperBoosts <= 0) return;

            TelemetryCache data = LoadData();
            data.cache_id += _sessionHyperBoosts;
            SaveData(data);

            Logger.LogInfo($"Banked {_sessionHyperBoosts} Hyper Boosts quietly.");
            _sessionHyperBoosts = 0;
        }

        private TelemetryCache LoadData()
        {
            if (!File.Exists(_saveFilePath)) return new TelemetryCache { cache_id = 0 };

            try
            {
                string b64 = File.ReadAllText(_saveFilePath);
                string json = Encoding.UTF8.GetString(Convert.FromBase64String(b64));
                return JsonUtility.FromJson<TelemetryCache>(json) ?? new TelemetryCache();
            }
            catch
            {
                return new TelemetryCache { cache_id = 0 };
            }
        }

        private void SaveData(TelemetryCache data)
        {
            try
            {
                string json = JsonUtility.ToJson(data);
                string b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
                File.WriteAllText(_saveFilePath, b64);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to save cache: {ex.Message}");
            }
        }
    }
}