using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Entry point: builds camera, light, UI, map, game and settings at runtime.
// No home screen: the game opens straight on the level map.
public static class App
{
    public static Canvas Canvas;
    public static MapScreen Map;
    public static GameController Game;
    public static SettingsPopup Settings;
    public static bool Mobile;   // phone/tablet browser: lighter effects + shadows

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (GameObject.Find("DuckSmashApp") != null) return;
        Application.targetFrameRate = 60;
        var root = new GameObject("DuckSmashApp");

        var cam = Camera.main;
        if (cam == null) { cam = new GameObject("Main Camera").AddComponent<Camera>(); cam.tag = "MainCamera"; }
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.97f, 0.85f, 0.9f);
        cam.nearClipPlane = 0.3f; cam.farClipPlane = 200;

        var sun = Object.FindObjectOfType<Light>();
        if (sun == null) sun = new GameObject("Sun").AddComponent<Light>();
        sun.type = LightType.Directional; sun.transform.rotation = Quaternion.Euler(58, -35, 0);
        // phone profile ("ds_forceMobile" pref lets us test it in the editor / on desktop)
        Mobile = Application.isMobilePlatform || PlayerPrefs.GetInt("ds_forceMobile", 0) == 1;
        // phones: no realtime shadow pass at all (ducks get a cheap blob instead), no HDR/MSAA buffers,
        // and no physics stepping (the game has its own 2D sim; Unity physics has nothing to do)
        cam.allowHDR = false; cam.allowMSAA = !Mobile;
        Physics.simulationMode = SimulationMode.Script; Physics2D.simulationMode = SimulationMode2D.Script;
        Physics.autoSyncTransforms = false;
        sun.intensity = 1.05f; sun.shadows = Mobile ? LightShadows.None : LightShadows.Soft; sun.shadowStrength = 0.55f; sun.color = new Color(1f, 0.97f, 0.92f);
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.62f, 0.6f, 0.72f);
        QualitySettings.shadowDistance = 32; QualitySettings.shadowCascades = 1;            // whole tub fits in one cascade
        QualitySettings.shadowResolution = Mobile ? ShadowResolution.Low : ShadowResolution.Medium; QualitySettings.shadowProjection = ShadowProjection.StableFit;
        QualitySettings.antiAliasing = Mobile ? 0 : QualitySettings.antiAliasing;
        QualitySettings.pixelLightCount = 1; QualitySettings.softParticles = false;

        if (Object.FindObjectOfType<EventSystem>() == null) new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));

        var cgo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        cgo.transform.SetParent(root.transform);
        Canvas = cgo.GetComponent<Canvas>(); Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = cgo.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920); scaler.matchWidthOrHeight = 0f;   // fit width: phones are tall, never clip sideways

        Sfx.Init(root);
        FxKit.Init(root.transform);
        Game = new GameObject("Game").AddComponent<GameController>(); Game.transform.SetParent(root.transform); Game.Init(cam, Canvas.transform);
        Map = new GameObject("Map").AddComponent<MapScreen>(); Map.transform.SetParent(root.transform); Map.Build(Canvas.transform);
        Settings = new GameObject("SettingsCtl").AddComponent<SettingsPopup>(); Settings.transform.SetParent(root.transform); Settings.Build(Canvas.transform);
        ShowMap();
        Game.StartCoroutine(Game.Prewarm());   // build block/duck templates in the background while the map is shown
    }

    public static void ShowMap() { Game.Close(); Map.Show(); }
    public static void StartLevel(int n) { Map.Hide(); Game.StartLevel(n); }
    public static void OpenSettings(bool inGame) => Settings.Open(inGame);
}
