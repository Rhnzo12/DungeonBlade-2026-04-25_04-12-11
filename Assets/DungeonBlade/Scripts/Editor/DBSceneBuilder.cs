#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.AI;
using DungeonBlade.Core;
using DungeonBlade.Dungeon;
using DungeonBlade.Inventory;
using DungeonBlade.Bank;
using DungeonBlade.Save;
using DungeonBlade.Progression;

namespace DungeonBlade.EditorTools
{
    /// <summary>
    /// Builds the Lobby and Dungeon scenes programmatically with
    /// full bootstrap/UI wiring and gray-box dungeon geometry.
    /// Output scenes: DungeonBladeSample/Scenes/Lobby.unity, Dungeon_ForsakenKeep.unity.
    /// </summary>
    public static class DBSceneBuilder
    {
        // ────────── Lobby ──────────
        public static void BuildLobbyScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Lighting — bumped intensity & ambient so the player capsule and
            // walls aren't washed out into near-black under URP without a skybox.
            MakeDirectionalLight(new Vector3(45, 35, 0), intensity: 1.7f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.6f, 0.65f, 0.75f);
            RenderSettings.ambientIntensity = 1.4f;

            // Floor
            var floor = BuildFloor("LobbyFloor", Vector3.zero, new Vector2(30, 30), new Color(0.15f, 0.18f, 0.22f));

            // Walls (simple room)
            BuildWall("Wall_N", new Vector3(0, 2, 15),  new Vector3(30, 4, 0.5f), new Color(0.2f, 0.22f, 0.28f));
            BuildWall("Wall_S", new Vector3(0, 2, -15), new Vector3(30, 4, 0.5f), new Color(0.2f, 0.22f, 0.28f));
            BuildWall("Wall_E", new Vector3(15, 2, 0),  new Vector3(0.5f, 4, 30), new Color(0.2f, 0.22f, 0.28f));
            BuildWall("Wall_W", new Vector3(-15, 2, 0), new Vector3(0.5f, 4, 30), new Color(0.2f, 0.22f, 0.28f));

            // Player spawn
            var spawn = new GameObject("PlayerSpawn");
            spawn.transform.position = new Vector3(0, 0, -8);

            // Bootstrap
            var bootstrap = BuildBootstrap();

            // Player instance
            var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(DBEditorMenu.PrefabsPath + "/Player.prefab");
            if (playerPrefab != null)
            {
                var player = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab);
                player.transform.position = spawn.transform.position;
                var gs = bootstrap.GetComponent<GameServices>();
                gs.playerRoot = player;
                LinkPlayerToServices(player, gs);
            }

            // Bank NPC
            GameObject bankNpcInstance = null;
            var bankNpcPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(DBEditorMenu.PrefabsPath + "/BankNPC.prefab");
            if (bankNpcPrefab != null)
            {
                bankNpcInstance = (GameObject)PrefabUtility.InstantiatePrefab(bankNpcPrefab);
                bankNpcInstance.transform.position = new Vector3(-5, 0, 8);
                // We'll wire bankUI + bankSystem refs AFTER Bank_Canvas is created below.
            }

            // Character Change NPC
            var charNPC = new GameObject("CharacterChangeNPC");
            charNPC.transform.position = new Vector3(5, 0, 8);
            var cnBody = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            cnBody.transform.SetParent(charNPC.transform, false);
            cnBody.transform.localPosition = new Vector3(0, 1, 0);
            var cnMat = DBMaterialHelper.Create(new Color(0.4f, 0.6f, 0.95f), new Color(0.2f, 0.3f, 0.5f));
            cnBody.GetComponent<Renderer>().sharedMaterial = cnMat;
            var cnTrigger = charNPC.AddComponent<BoxCollider>();
            cnTrigger.isTrigger = true;
            cnTrigger.size = new Vector3(3, 3, 3);
            cnTrigger.center = new Vector3(0, 1, 0);
            charNPC.AddComponent<DungeonBlade.Characters.CharacterChangeNPC>();

            // Off-screen 3D character preview rig (camera + spawn point + RT).
            // Lives 100m away on the X axis so its render output doesn't pollute
            // the gameplay camera. CharacterSelectUI.previewStage is wired below.
            var previewStage = BuildCharacterPreviewStage();

            // Inventory + Bank UI canvases
            var invCanvas = DBHUDBuilder.BuildInventoryCanvas();
            invCanvas.GetComponent<DungeonBlade.UI.InventoryUI>().inventory = bootstrap.GetComponent<InventorySystem>();
            // Also add bank UI stub here — wire BankNPC
            BuildBankUI(bootstrap);

            // Wire BankNPC refs now that Bank_Canvas exists
            if (bankNpcInstance != null)
            {
                var bankNpcComp = bankNpcInstance.GetComponent<DungeonBlade.UI.BankNPC>();
                var bankCanvasGO = GameObject.Find("Bank_Canvas");
                if (bankNpcComp != null && bankCanvasGO != null)
                {
                    bankNpcComp.bankUI = bankCanvasGO.GetComponent<DungeonBlade.UI.BankUI>();
                    bankNpcComp.bankSystem = bootstrap.GetComponent<BankSystem>();
                    // Find the Prompt child and set as promptUI
                    var promptChild = bankNpcInstance.transform.Find("Prompt");
                    if (promptChild != null) bankNpcComp.promptUI = promptChild.gameObject;
                }
            }

            // Character select canvas — auto-opens on every Play in Lobby
            var selectCanvasGO = DBHUDBuilder.BuildCharacterSelectCanvas();
            var selectUI = selectCanvasGO.GetComponent<DungeonBlade.Characters.CharacterSelectUI>();
            if (selectUI != null && previewStage != null)
                selectUI.previewStage = previewStage.GetComponent<DungeonBlade.Characters.CharacterPreviewStage>();

            // EventSystem — required for UI clicks to register
            EnsureEventSystem();

            // Mark static geometry so NavMesh can bake (lobby doesn't need one but consistent)
            foreach (var r in UnityEngine.Object.FindObjectsOfType<Renderer>())
                if (r.gameObject.name.StartsWith("Wall") || r.gameObject.name.Contains("Floor"))
                    GameObjectUtility.SetStaticEditorFlags(r.gameObject, StaticEditorFlags.ContributeGI | StaticEditorFlags.NavigationStatic);

            // Save
            string path = DBEditorMenu.ScenesPath + "/Lobby.unity";
            EditorSceneManager.SaveScene(scene, path);
            AssetDatabase.Refresh();
        }

        // ────────── Dungeon ──────────
        public static void BuildDungeonScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Lighting — moody
            MakeDirectionalLight(new Vector3(50, 45, 10), intensity: 0.6f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.15f, 0.16f, 0.22f);
            RenderSettings.fog = true;
            RenderSettings.fogColor = new Color(0.08f, 0.1f, 0.14f);
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = 15;
            RenderSettings.fogEndDistance = 80;

            var stoneColor = new Color(0.25f, 0.23f, 0.22f);
            var floorColor = new Color(0.18f, 0.17f, 0.16f);
            var bridgeColor = new Color(0.3f, 0.28f, 0.22f);

            // ───── ZONE 1: Gate Hall (z=0..30) ─────
            BuildFloor("Z1_Floor", new Vector3(0, 0, 15),  new Vector2(18, 30), floorColor);
            BuildWall("Z1_WallW", new Vector3(-9, 2.5f, 15), new Vector3(0.5f, 5, 30), stoneColor);
            BuildWall("Z1_WallE", new Vector3( 9, 2.5f, 15), new Vector3(0.5f, 5, 30), stoneColor);
            BuildWall("Z1_WallS", new Vector3(0, 2.5f, 0),   new Vector3(18, 5, 0.5f), stoneColor);

            var spawn = new GameObject("PlayerSpawn");
            spawn.transform.position = new Vector3(0, 0, 2);

            SpawnEnemy("SkeletonSoldier", new Vector3(-4, 0, 14));
            SpawnEnemy("SkeletonSoldier", new Vector3( 4, 0, 14));
            SpawnEnemy("SkeletonSoldier", new Vector3(-3, 0, 22));
            SpawnEnemy("SkeletonSoldier", new Vector3( 3, 0, 22));

            BuildCheckpoint("Checkpoint_Z1", new Vector3(0, 0, 28), "zone_1_end", 1);

            // ───── ZONE 2: Barracks (z=30..58) ─────
            BuildFloor("Z2_Floor", new Vector3(0, 0, 44), new Vector2(22, 28), floorColor);
            BuildWall("Z2_WallW", new Vector3(-11, 2.5f, 44), new Vector3(0.5f, 5, 28), stoneColor);
            BuildWall("Z2_WallE", new Vector3( 11, 2.5f, 44), new Vector3(0.5f, 5, 28), stoneColor);

            // Upper floor sections (elevation intro)
            BuildFloor("Z2_UpperFloor", new Vector3(-5, 2.5f, 50), new Vector2(8, 12), new Color(0.22f, 0.2f, 0.19f));
            BuildWall("Z2_UpperRail", new Vector3(-1.5f, 3f, 50), new Vector3(0.3f, 1, 12), stoneColor);

            // Obstacles / pillars
            BuildWall("Z2_Pillar1", new Vector3(3, 2, 38),  new Vector3(1.5f, 4, 1.5f), stoneColor);
            BuildWall("Z2_Pillar2", new Vector3(-3, 2, 44), new Vector3(1.5f, 4, 1.5f), stoneColor);
            BuildWall("Z2_Pillar3", new Vector3(5, 2, 52),  new Vector3(1.5f, 4, 1.5f), stoneColor);

            SpawnEnemy("SkeletonArcher", new Vector3(-5, 2.5f, 50));  // on upper floor
            SpawnEnemy("SkeletonArcher", new Vector3( 8, 0, 40));
            SpawnEnemy("SkeletonArcher", new Vector3( 6, 0, 54));
            SpawnEnemy("SkeletonSoldier",new Vector3(-4, 0, 42));
            SpawnEnemy("SkeletonSoldier",new Vector3( 3, 0, 48));
            SpawnEnemy("SkeletonSoldier",new Vector3(-2, 0, 54));
            SpawnEnemy("SkeletonSoldier",new Vector3( 0, 0, 56));

            // ───── ZONE 3: The Bridge (z=58..90) ─────
            // Narrow bridge with a gap in the middle — wall run required
            BuildFloor("Z3_BridgeA", new Vector3(0, 0, 66), new Vector2(6, 14), bridgeColor);
            // GAP — z=73..78
            BuildFloor("Z3_BridgeB", new Vector3(0, 0, 85), new Vector2(6, 12), bridgeColor);
            // Side ledges to wall-run along
            BuildWall("Z3_LedgeW", new Vector3(-3.5f, 1.5f, 75), new Vector3(0.5f, 3, 14), stoneColor);
            BuildWall("Z3_LedgeE", new Vector3( 3.5f, 1.5f, 75), new Vector3(0.5f, 3, 14), stoneColor);
            // Fall pit below the gap
            var pit = new GameObject("Z3_FallPit");
            pit.transform.position = new Vector3(0, -2, 75);
            pit.transform.localScale = new Vector3(8, 1, 10);
            var pitCol = pit.AddComponent<BoxCollider>();
            pitCol.isTrigger = true;
            pit.AddComponent<FallPit>();

            SpawnEnemy("SkeletonArcher", new Vector3(-2, 0, 88));
            SpawnEnemy("SkeletonArcher", new Vector3( 2, 0, 88));
            SpawnEnemy("ArmoredKnight",  new Vector3( 0, 0, 90));  // bridge ambush elite — counts as zone 3 in GDD though

            BuildCheckpoint("Checkpoint_Z3", new Vector3(0, 0, 89), "zone_3_end", 3);

            // ───── ZONE 4: Armory (z=90..110) ─────
            BuildFloor("Z4_Floor", new Vector3(0, 0, 100), new Vector2(18, 20), floorColor);
            BuildWall("Z4_WallW", new Vector3(-9, 2.5f, 100), new Vector3(0.5f, 5, 20), stoneColor);
            BuildWall("Z4_WallE", new Vector3( 9, 2.5f, 100), new Vector3(0.5f, 5, 20), stoneColor);

            SpawnEnemy("ArmoredKnight", new Vector3(0, 0, 100));   // elite guard

            // Loot pickup (placeholder weapon)
            var lootTable = AssetDatabase.LoadAssetAtPath<DungeonBlade.Loot.LootTable>(DBEditorMenu.LootPath + "/Loot_ArmoredKnight.asset");
            // (Loot will be handled by the knight's loot drop on death.)

            // Trap: spike tile
            BuildSpikeTrap(new Vector3(0, 0.05f, 95));

            // ───── ZONE 5: Throne Room (z=110..150) ─────
            BuildFloor("Z5_Floor", new Vector3(0, 0, 130), new Vector2(36, 40), floorColor);
            BuildWall("Z5_WallW", new Vector3(-18, 3, 130), new Vector3(0.5f, 6, 40), stoneColor);
            BuildWall("Z5_WallE", new Vector3( 18, 3, 130), new Vector3(0.5f, 6, 40), stoneColor);
            BuildWall("Z5_WallN", new Vector3(0, 3, 150),   new Vector3(36, 6, 0.5f), stoneColor);

            // Cover pillars per GDD 3.2 ("Large circular room with pillars for cover")
            for (int i = 0; i < 6; i++)
            {
                float angle = i * Mathf.PI * 2f / 6f;
                float r = 10f;
                BuildWall($"Z5_Pillar_{i}",
                    new Vector3(Mathf.Cos(angle) * r, 3, 130 + Mathf.Sin(angle) * r),
                    new Vector3(2, 6, 2), stoneColor);
            }

            // Boss
            var bossPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(DBEditorMenu.PrefabsPath + "/UndeadWarlord.prefab");
            if (bossPrefab != null)
            {
                var boss = (GameObject)PrefabUtility.InstantiatePrefab(bossPrefab);
                boss.transform.position = new Vector3(0, 0, 140);
                var lt = AssetDatabase.LoadAssetAtPath<DungeonBlade.Loot.LootTable>(DBEditorMenu.LootPath + "/Loot_Boss_ForsakenKeep.asset");
                var bossScript = boss.GetComponent<DungeonBlade.Enemies.UndeadWarlord>();
                if (bossScript != null && lt != null) bossScript.lootTable = lt;
                // Connect boss defeated -> DungeonManager
                // (Will be wired when bootstrap hookup runs below.)
            }

            // ───── Bootstrap, Player, HUD ─────
            var bootstrap = BuildBootstrap();

            var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(DBEditorMenu.PrefabsPath + "/Player.prefab");
            if (playerPrefab != null)
            {
                var player = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab);
                player.transform.position = spawn.transform.position;
                var gs = bootstrap.GetComponent<GameServices>();
                gs.playerRoot = player;
                LinkPlayerToServices(player, gs);
            }

            // HUD
            var hud = DBHUDBuilder.BuildHUD();
            var hudMgr = hud.GetComponent<DungeonBlade.UI.HUDManager>();
            var playerGO = GameObject.FindGameObjectWithTag("Player");
            if (playerGO != null)
            {
                hudMgr.playerStats = playerGO.GetComponent<DungeonBlade.Player.PlayerStats>();
                hudMgr.playerCombat = playerGO.GetComponent<DungeonBlade.Player.PlayerCombat>();
                hudMgr.playerSkills = playerGO.GetComponent<DungeonBlade.Player.PlayerSkills>();
                hudMgr.comboCounter = playerGO.GetComponent<DungeonBlade.Combat.ComboCounter>();
            }
            hudMgr.progression = bootstrap.GetComponent<PlayerProgression>();

            // Inventory Canvas
            var invCanvas = DBHUDBuilder.BuildInventoryCanvas();
            invCanvas.GetComponent<DungeonBlade.UI.InventoryUI>().inventory = bootstrap.GetComponent<InventorySystem>();

            // Mark static
            foreach (var r in UnityEngine.Object.FindObjectsOfType<Renderer>())
                if (r.gameObject.name.StartsWith("Z") || r.gameObject.name.Contains("Floor") || r.gameObject.name.Contains("Wall") || r.gameObject.name.Contains("Pillar") || r.gameObject.name.Contains("Ledge") || r.gameObject.name.Contains("Bridge"))
                    GameObjectUtility.SetStaticEditorFlags(r.gameObject, StaticEditorFlags.ContributeGI | StaticEditorFlags.NavigationStatic | StaticEditorFlags.BatchingStatic);

            // NavMesh: bake at build time so enemy NavMeshAgents can pathfind
            // without requiring the user to manually open Window → AI → Navigation
            // and click Bake. Uses the AI Navigation package (NavMeshSurface).
            BuildNavMeshSurfaceAndBake();

            // Zone labels (readable text floating in each zone to help navigation)
            MakeZoneLabel("1 · GATE HALL",    new Vector3(0, 5, 15));
            MakeZoneLabel("2 · BARRACKS",     new Vector3(0, 5, 44));
            MakeZoneLabel("3 · THE BRIDGE",   new Vector3(0, 5, 75));
            MakeZoneLabel("4 · ARMORY",       new Vector3(0, 5, 100));
            MakeZoneLabel("5 · THRONE ROOM",  new Vector3(0, 7, 130));

            // EventSystem — required for UI clicks (inventory, pause menus, etc)
            EnsureEventSystem();

            // Save
            string path = DBEditorMenu.ScenesPath + "/Dungeon_ForsakenKeep.unity";
            EditorSceneManager.SaveScene(scene, path);
            AssetDatabase.Refresh();
        }

        // Adds a NavMeshSurface to a NavMesh root GameObject and bakes it.
        // Uses reflection so this code compiles even if AI Navigation isn't
        // installed — falls back to logging a hint for the user to bake manually.
        static void BuildNavMeshSurfaceAndBake()
        {
            var surfaceType = System.Type.GetType("Unity.AI.Navigation.NavMeshSurface, Unity.AI.Navigation");
            if (surfaceType == null)
            {
                Debug.LogWarning("[DBSceneBuilder] AI Navigation package not found. " +
                                 "Bake the NavMesh manually via Window → AI → Navigation → Bake.");
                return;
            }
            var holder = new GameObject("NavMesh");
            var surface = (Component)holder.AddComponent(surfaceType);
            // Default: collect all geometry in the scene that's marked NavigationStatic.
            // Trigger a build by calling BuildNavMesh().
            var buildMethod = surfaceType.GetMethod("BuildNavMesh");
            if (buildMethod != null)
            {
                buildMethod.Invoke(surface, null);
                Debug.Log("[DBSceneBuilder] Baked NavMesh for dungeon scene.");
            }
            else
            {
                Debug.LogWarning("[DBSceneBuilder] NavMeshSurface.BuildNavMesh() not found via reflection — " +
                                 "bake manually by selecting NavMesh and clicking Bake in the inspector.");
            }
        }

        // ────────── Helpers ──────────
        static void EnsureEventSystem()
        {
            var existing = UnityEngine.Object.FindObjectOfType<EventSystem>();
            if (existing != null) return;

            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
            go.AddComponent<StandaloneInputModule>();
        }

        static GameObject BuildBootstrap()
        {
            var go = new GameObject("_GameBootstrap");

            var gs = go.AddComponent<GameServices>();
            var inv = go.AddComponent<InventorySystem>();
            var bank = go.AddComponent<BankSystem>();
            var save = go.AddComponent<SaveSystem>();
            var prog = go.AddComponent<PlayerProgression>();
            var dm = go.AddComponent<DungeonManager>();
            var gm = go.AddComponent<GameManager>();
            var roster = go.AddComponent<DungeonBlade.Characters.CharacterRoster>();

            gs.inventory = inv;
            gs.bank = bank;
            gs.save = save;
            gs.progression = prog;
            gs.dungeon = dm;
            gs.roster = roster;
            bank.playerInventory = inv;
            save.inventory = inv;
            save.bank = bank;
            save.progression = prog;

            // Populate character roster from discovered CharacterData assets
            roster.characters = DBCharacterBuilder.LoadAllCharacters();
            if (roster.characters.Count > 0) roster.defaultCharacter = roster.characters[0];

            // Populate item registry
            var guids = AssetDatabase.FindAssets("t:ItemData", new[] { DBEditorMenu.ItemsPath });
            var items = new System.Collections.Generic.List<DungeonBlade.Items.ItemData>();
            foreach (var g in guids)
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                var it = AssetDatabase.LoadAssetAtPath<DungeonBlade.Items.ItemData>(p);
                if (it != null) items.Add(it);
            }
            save.itemRegistry = items.ToArray();

            // Populate shop
            bank.shopItems = DBContentBuilder.BuildShopEntries();
            bank.tokenShopItems = DBContentBuilder.BuildTokenShopEntries();

            return go;
        }

        static void LinkPlayerToServices(GameObject player, GameServices gs)
        {
            var stats = player.GetComponent<DungeonBlade.Player.PlayerStats>();
            var combat = player.GetComponent<DungeonBlade.Player.PlayerCombat>();
            if (gs.inventory != null)
            {
                gs.inventory.playerStats = stats;
                gs.inventory.playerCombat = combat;
            }
            if (gs.progression != null) gs.progression.playerStats = stats;
            if (gs.dungeon != null)
            {
                gs.dungeon.playerStats = stats;
                gs.dungeon.comboCounter = player.GetComponent<DungeonBlade.Combat.ComboCounter>();
            }
        }

        static GameObject BuildFloor(string name, Vector3 pos, Vector2 size, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.position = pos;
            go.transform.localScale = new Vector3(size.x, 0.2f, size.y);
            go.transform.position = new Vector3(pos.x, pos.y - 0.1f, pos.z);
            ApplyColor(go, color);
            return go;
        }

        static GameObject BuildWall(string name, Vector3 pos, Vector3 size, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.position = pos;
            go.transform.localScale = size;
            ApplyColor(go, color);
            return go;
        }

        static void MakeDirectionalLight(Vector3 eulerAngles, float intensity = 1.0f)
        {
            var go = new GameObject("Directional Light");
            go.transform.eulerAngles = eulerAngles;
            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = intensity;
            light.shadows = LightShadows.Soft;
            light.color = new Color(1f, 0.95f, 0.85f);
        }

        static void SpawnEnemy(string prefabName, Vector3 pos)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(DBEditorMenu.PrefabsPath + "/" + prefabName + ".prefab");
            if (prefab == null) return;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.transform.position = pos;
        }

        static void BuildCheckpoint(string name, Vector3 pos, string id, int zone)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            go.transform.position = new Vector3(pos.x, pos.y + 0.1f, pos.z);
            go.transform.localScale = new Vector3(2f, 0.1f, 2f);
            var col = go.GetComponent<Collider>();
            col.isTrigger = true;
            ApplyColor(go, new Color(0.3f, 0.8f, 1f), emissive: new Color(0.3f, 0.8f, 1f));

            var cp = go.AddComponent<Checkpoint>();
            cp.checkpointId = id;
            cp.zoneIndex = zone;
            var respawn = new GameObject("RespawnPoint");
            respawn.transform.SetParent(go.transform, false);
            respawn.transform.localPosition = Vector3.up * 0.5f;
            cp.respawnPoint = respawn.transform;
        }

        static void BuildSpikeTrap(Vector3 pos)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "SpikeTrap";
            go.transform.position = pos;
            go.transform.localScale = new Vector3(3, 0.1f, 3);
            var col = go.GetComponent<Collider>();
            col.isTrigger = true;
            ApplyColor(go, new Color(0.3f, 0.3f, 0.35f));

            var spikes = GameObject.CreatePrimitive(PrimitiveType.Cube);
            spikes.name = "Spikes";
            spikes.transform.SetParent(go.transform, false);
            UnityEngine.Object.DestroyImmediate(spikes.GetComponent<Collider>());
            spikes.transform.localScale = new Vector3(1f, 5f, 1f);
            spikes.transform.localPosition = new Vector3(0, 0, 0);
            ApplyColor(spikes, new Color(0.4f, 0.3f, 0.3f));

            var trap = go.AddComponent<SpikeTrap>();
            trap.spikes = spikes.transform;
            var warn = new GameObject("WarnMarker");
            warn.transform.SetParent(go.transform, false);
            var warnRenderer = go.GetComponent<Renderer>();
            trap.warnRenderer = warnRenderer;
        }

        static void MakeZoneLabel(string text, Vector3 pos)
        {
            var go = new GameObject("ZoneLabel_" + text);
            go.transform.position = pos;
            var tm = go.AddComponent<TextMesh>();
            tm.text = text;
            tm.fontSize = 40;
            tm.alignment = TextAlignment.Center;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.characterSize = 0.08f;
            tm.color = new Color(0.6f, 0.75f, 1f);
            // Always face the camera so text isn't mirrored when viewed from behind
            go.AddComponent<DungeonBlade.Runtime.Billboard>();
        }

        static void ApplyColor(GameObject go, Color color, Color? emissive = null)
        {
            var r = go.GetComponent<Renderer>();
            if (r == null) return;
            var mat = DBMaterialHelper.Create(color, emissive);
            r.sharedMaterial = mat;
        }

        /// <summary>
        /// Off-screen rig the CharacterSelectUI samples for the live 3D model
        /// preview. Stage root sits 100m off the gameplay area so its mesh and
        /// camera never bleed into the player view.
        /// </summary>
        static GameObject BuildCharacterPreviewStage()
        {
            var stage = new GameObject("CharacterPreviewStage");
            stage.transform.position = new Vector3(100, 0, 0);

            // Spawn point — where each character model is parented.
            var spawn = new GameObject("SpawnPoint");
            spawn.transform.SetParent(stage.transform, false);
            spawn.transform.localPosition = Vector3.zero;

            // Dedicated camera (child) — the stage component points it
            // at chest height after framing in Awake.
            var camGO = new GameObject("PreviewCamera");
            camGO.transform.SetParent(stage.transform, false);
            camGO.transform.localPosition = new Vector3(0, 1.15f, -3.2f);
            var cam = camGO.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.05f, 0.06f, 0.10f, 1f);
            cam.fieldOfView = 35f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 30f;
            cam.depth = -2;

            // Soft fill light so the model isn't a silhouette.
            var lightGO = new GameObject("PreviewFill");
            lightGO.transform.SetParent(stage.transform, false);
            lightGO.transform.localPosition = new Vector3(2, 3, -2);
            lightGO.transform.localEulerAngles = new Vector3(35, -25, 0);
            var fill = lightGO.AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.intensity = 1.3f;
            fill.color = new Color(1f, 0.97f, 0.9f);
            fill.cullingMask = ~0;

            var stageComp = stage.AddComponent<DungeonBlade.Characters.CharacterPreviewStage>();
            stageComp.previewCamera = cam;
            stageComp.spawnPoint = spawn.transform;
            return stage;
        }

        static void BuildBankUI(GameObject bootstrap)
        {
            // Minimal bank UI — can be replaced by dedicated canvas
            var canvas = new GameObject("Bank_Canvas");
            var c = canvas.AddComponent<Canvas>();
            c.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.AddComponent<UnityEngine.UI.CanvasScaler>();
            canvas.AddComponent<UnityEngine.UI.GraphicRaycaster>();

            var bankUI = canvas.AddComponent<DungeonBlade.UI.BankUI>();
            bankUI.bankSystem = bootstrap.GetComponent<BankSystem>();

            var root = new GameObject("Root");
            root.transform.SetParent(canvas.transform, false);
            root.AddComponent<UnityEngine.UI.Image>().color = new Color(0.05f, 0.05f, 0.1f, 0.95f);
            var rt = root.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.1f, 0.1f); rt.anchorMax = new Vector2(0.9f, 0.9f);
            rt.sizeDelta = Vector2.zero; rt.anchoredPosition = Vector2.zero;
            root.SetActive(false);
            bankUI.rootPanel = root;

            // Wire to any BankNPC in the scene
            var npc = UnityEngine.Object.FindObjectOfType<DungeonBlade.UI.BankNPC>();
            if (npc != null)
            {
                npc.bankSystem = bankUI.bankSystem;
                npc.bankUI = bankUI;
            }
        }
    }
}
#endif
