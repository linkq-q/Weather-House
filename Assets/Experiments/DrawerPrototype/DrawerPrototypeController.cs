using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace WeatherHouse.DrawerPrototype
{
    public sealed partial class DrawerPrototypeController : MonoBehaviour
    {
        public const string SaveOverride = "WEATHERHOUSE_DRAWER_PROTOTYPE_SAVE";
        public Camera ViewCamera { get; private set; }
        public DrawerProgress Progress { get; private set; }
        public DrawerToken[] Items { get; } = new DrawerToken[5];
        public List<DrawerToken> Papers { get; } = new List<DrawerToken>();
        public int SelectedItem => selected ? selected.item : -1;
        public bool Busy => busy;
        public bool Automation { get; set; }
        public bool Dragging => dragging;
        public LineRenderer PaperOutline { get; private set; }
        public string SavePath { get; private set; }
        public string SaveError { get; private set; }
        public RectTransform CloseTarget => closeTarget;
        [Header("Scene materials and plants · existing game assets")]
        public Texture2D timberAlbedo, paperGrain, paperNormal;
        [Header("Existing Waterside meadow assets")]
        public Mesh[] meadowVariants;
        public Material[] meadowMaterials;
        public Material meadowGround;
        public TerrainLayer meadowLayer;
        public GameObject[] meadowPlants;
        public WeatherHouse.Hillside.HillsideGrassRenderer Meadow { get; private set; }
        [Header("Live lighting · adjust during Play")]
        [Range(.2f,2f)] public float sunlight = 1.08f;
        [Range(25,85)] public float sunElevation = 60;
        [Range(-180,180)] public float sunAzimuth = -38;
        [Range(0,1)] public float shadowStrength = .58f;
        public Color sunlightColor = new Color(1f,.98f,.93f);
        public Color ambientColor = new Color(.65f,.60f,.52f);
        Light drawerSun;

        static readonly string[] Titles = { "魔法笔记", "字典", "相机", "魔杖", "奶奶的信" };
        static readonly string[] Descriptions =
        {
            "里面用动物语言记着天气魔法。\n边学习，边练习。",
            "把遇到的动物字记在这里，\n慢慢读懂它们的意思。",
            "把发现拍下来，\n留着慢慢观察。",
            "你已经会一点天气魔法，\n还有许多需要练习。",
            "出门后，拜托去看看小狗。\n照片里的就是它。"
        };
        // Rectangles are top-left pixel coordinates in the approved 1760 x 880 atlas.
        static readonly Rect[] Crops =
        {
            new Rect(98,82,297,374), new Rect(520,79,292,377), new Rect(939,164,333,241),
            new Rect(1380,90,330,365), new Rect(60,520,380,315), new Rect(505,578,325,215),
            new Rect(941,578,325,215), new Rect(1342,616,386,120)
        };

        readonly List<UnityEngine.Object> generated = new List<UnityEngine.Object>();
        Texture2D atlas;
        Material paperSide, paperTop, paperFold, crease, green, sage, wood, dark, cream, glowMaterial;
        PhysicMaterial friction;
        Canvas canvas;
        RectTransform dialog, closeTarget, completePanel, hint;
        Text title, description;
        Font font;
        AudioSource sound;
        AudioClip paperSound, pickupSound;
        DrawerToken pressed, dragging, selected;
        Vector2 pressScreen;
        Vector3 dragOffset, dragTarget;
        Quaternion dragRotation;
        Material paperHighlight;
        float pressTime, dragHeight;
        bool busy, readOnly;
        GameObject magic;
        readonly List<Transform> stars = new List<Transform>();

        void Start()
        {
            SavePath = Environment.GetEnvironmentVariable(SaveOverride);
            if (string.IsNullOrEmpty(SavePath)) SavePath = Path.Combine(Application.persistentDataPath, "DrawerPrototype", "progress.json");
            Progress = new DrawerProgress();
            try { if (File.Exists(SavePath)) Progress = DrawerProgress.Restore(File.ReadAllText(SavePath)); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException)
            { SaveError = e.Message; readOnly = true; Debug.LogWarning("Drawer progress was preserved after a read failure: " + e.Message); }
            atlas = Resources.Load<Texture2D>("DrawerPrototype/Props");
            font = Resources.Load<Font>("Fonts/ZCOOLXiaoWei-Regular");
            if (!atlas || !timberAlbedo || !paperGrain || !font) throw new InvalidOperationException("Drawer scene assets are missing. Rebuild the independent drawer scene.");
            InitializeLook();
            BuildMaterials();
            BuildStage();
            BuildItems();
            BuildPapers();
            BuildInterface();
            BuildMagic();
            BuildPaperFeedback();
            ApplyLookSettings();
            sound = gameObject.AddComponent<AudioSource>();
            sound.playOnAwake = false; sound.spatialBlend = 0; sound.volume = .42f;
            paperSound = Resources.Load<AudioClip>("LifeAudio/Clips/paper_move");
            pickupSound = Resources.Load<AudioClip>("LifeAudio/Clips/progress_soft");
            RefreshCompletion();
            StartCoroutine(ResumeUnread());
        }

        IEnumerator ResumeUnread()
        {
            yield return new WaitForSeconds(.8f);
            for (int i = 0; i < Items.Length; i++)
                if (Progress.collected[i] && !Progress.read[i]) { OpenItem(Items[i]); yield break; }
        }

        void BuildMaterials()
        {
            paperSide = RuntimeLook.paper; paperTop = RuntimeLook.paper; paperFold = RuntimeLook.paperFold;
            crease = Paint("Fine folded paper crease",new Color(.69f,.64f,.53f),true);
            cream = Paint("Cream", new Color(.87f,.86f,.74f));
            green = RuntimeLook.magicBook; sage = RuntimeLook.dictionary;
            wood = Paint("Wand wood", new Color(.37f,.25f,.15f));
            wood.SetTexture("_BaseMap",timberAlbedo);
            dark = Paint("Lens charcoal", new Color(.16f,.19f,.18f));
            glowMaterial = Paint("Ivory magic", new Color(1f,.94f,.65f), true);
            friction = new PhysicMaterial("Drawer paper friction") { dynamicFriction = .32f, staticFriction = .42f, bounciness = .02f };
            generated.Add(friction);
        }

        Material Paint(string label, Color color, bool unlit = false)
        {
            var shader = Shader.Find(unlit ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Lit");
            if (!shader) throw new InvalidOperationException("The project's URP shader is unavailable.");
            var material = new Material(shader) { name = label };
            material.SetColor("_BaseColor", color); material.SetFloat("_Smoothness", 0);
            material.SetFloat("_SpecularHighlights", 1); material.SetFloat("_EnvironmentReflections", 0);
            generated.Add(material); return material;
        }

        void Normal(Material material, Texture2D texture, float strength)
        { if (texture) { material.SetTexture("_BumpMap",texture); material.SetFloat("_BumpScale",strength); material.EnableKeyword("_NORMALMAP"); } }

        Material Printed(int crop)
        {
            var material = Paint("Drawer printed surface " + crop, Color.white);
            material.SetTexture("_BaseMap", atlas);
            material.SetFloat("_AlphaClip", 1); material.SetFloat("_Cutoff", .25f);
            material.EnableKeyword("_ALPHATEST_ON"); material.renderQueue = 2450;
            material.SetFloat("_Cull", 0);
            return material;
        }

        void BuildStage()
        {
            var cameraObject = new GameObject("Drawer · gently tilted overhead camera");
            cameraObject.transform.SetParent(transform);
            Quaternion viewAngle = Quaternion.Euler(78,0,0);
            Vector3 focus = DrawerCenter+Vector3.up*.36f;
            cameraObject.transform.SetPositionAndRotation(focus-viewAngle*Vector3.forward*20,viewAngle);
            ViewCamera = cameraObject.AddComponent<Camera>();
            ViewCamera.orthographic = true; ViewCamera.orthographicSize = 4.5f;
            ViewCamera.nearClipPlane = .1f; ViewCamera.farClipPlane = 40;
            ViewCamera.clearFlags = CameraClearFlags.SolidColor; ViewCamera.backgroundColor = new Color(.17f,.19f,.15f);
            var cameraData = cameraObject.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            cameraData.renderPostProcessing = true;
            cameraData.antialiasing = UnityEngine.Rendering.Universal.AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            cameraData.antialiasingQuality = UnityEngine.Rendering.Universal.AntialiasingQuality.High;
            cameraObject.AddComponent<AudioListener>();
            OilPost = cameraObject.AddComponent<WeatherHouse.MaterialLab.OilPaintPostSettings>();
            OilPost.pixelEnabled = false; OilPost.oilEnabled = false;
            var lightObject = new GameObject("Drawer · leaf-filtered daylight");
            lightObject.transform.SetParent(transform); lightObject.transform.rotation = Quaternion.Euler(65,-35,0);
            drawerSun = lightObject.AddComponent<Light>(); drawerSun.type = LightType.Directional;
            drawerSun.shadows = LightShadows.Soft; drawerSun.shadowBias = .025f; drawerSun.shadowNormalBias = .025f;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.skybox = null; ApplyLighting();
            BuildMeadow();
            BuildDrawer(timberAlbedo);
        }

        void Wall(Vector3 position, Vector3 size)
        {
            var wall = new GameObject("Drawer solid wall"); wall.transform.SetParent(transform); wall.transform.position = position;
            wall.AddComponent<BoxCollider>().size = size;
        }

        void BuildItems()
        {
            Items[0] = MakeToken(0, new Vector3(-1.2f,.36f,1.0f), new Vector3(1.76f,.44f,2.22f), -12);
            Items[1] = MakeToken(1, new Vector3(2.5f,.36f,1.75f), new Vector3(1.65f,.42f,2.10f), 11);
            Items[2] = MakeToken(2, new Vector3(2.55f,.36f,-1.60f), new Vector3(2.23f,.91f,1.42f), -9);
            Items[3] = MakeToken(3, new Vector3(-3.55f,.28f,-1.0f), new Vector3(.29f,.27f,3.15f), -35);
            Items[4] = MakeToken(4, new Vector3(-1.0f,.24f,-1.62f), new Vector3(2.20f,.18f,2.20f), 8);
            for (int i = 0; i < Items.Length; i++)
            {
                DrawerToken item = Items[i];
                if (i == 0 || i == 1)
                {
                    BuildBook(item.transform,item.dimensions,i);
                }
                else if (i == 2)
                {
                    item.hitBox.center = new Vector3(0,.19f,0);
                    item.hitBox.size = new Vector3(2.23f,.91f,1.42f);
                    BuildCameraModel(item.transform);
                }
                else if (i == 3) BuildWand(item.transform);
                else
                {
                    item.hitBox.center = new Vector3(0,.07f,.025f);
                    BuildGrandmotherLetter(item.transform);
                }
                if (Progress.collected[i]) DockImmediately(item);
            }
            AlbumModel = new GameObject("Album task book · model only"); AlbumModel.transform.SetParent(transform,false);
            BuildBook(AlbumModel.transform,new Vector3(1.76f,.44f,2.22f),2); AlbumModel.SetActive(false);
        }

        DrawerToken MakeToken(int id, Vector3 position, Vector3 dimensions, float yaw)
        {
            var root = new GameObject(id < 0 ? "Movable white envelope" : Titles[id]); root.transform.SetParent(transform);
            root.transform.SetPositionAndRotation(position, Quaternion.Euler(0,yaw,0));
            var token = root.AddComponent<DrawerToken>(); token.item = id; token.dimensions = dimensions;
            token.hitBox = root.AddComponent<BoxCollider>(); token.hitBox.size = dimensions; token.hitBox.sharedMaterial = friction;
            token.body = root.AddComponent<Rigidbody>(); token.body.mass = id < 0 ? .045f : .25f;
            token.body.drag = 3.2f; token.body.angularDrag = 6; token.body.maxAngularVelocity = 4;
            token.body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            token.body.interpolation = RigidbodyInterpolation.Interpolate;
            token.body.solverIterations = 10; token.body.solverVelocityIterations = 5;
            return token;
        }

        void BuildPapers()
        {
            var random = new System.Random(2110);
            for (int row = 0; row < 3; row++) for (int column = 0; column < 4; column++)
            {
                float x = -3.6f + column * 2.15f + ((float)random.NextDouble()-.5f)*.4f;
                float z = -1.9f + row * 1.8f + ((float)random.NextDouble()-.5f)*.35f;
                AddPaper(new Vector3(x,.60f+row*.09f,z), 2.30f, random.Next(-30,30), row+column);
            }
            Vector2[] cover =
            {
                new Vector2(-3.65f,2.0f), new Vector2(-1.1f,2.12f), new Vector2(1.3f,2.5f), new Vector2(3.4f,2.55f),
                new Vector2(-3.8f,.15f), new Vector2(-1.65f,.7f), new Vector2(.8f,.95f), new Vector2(3.15f,-.2f),
                new Vector2(-3.7f,-1.95f), new Vector2(-1.45f,-1.5f), new Vector2(.75f,-1.8f), new Vector2(3.1f,-2.45f),
                new Vector2(-.15f,-.25f), new Vector2(1.7f,-.55f)
            };
            for (int i = 0; i < cover.Length; i++) AddPaper(new Vector3(cover[i].x,1.05f+i*.065f,cover[i].y),2.20f+(float)random.NextDouble()*.28f,random.Next(-28,28),i);
        }

        void AddPaper(Vector3 position, float width, float yaw, int variant)
        {
            var dimensions = new Vector3(width,.018f,width*.66f);
            DrawerToken paper = MakeToken(-1,position,dimensions,yaw);
            paper.hitBox.center = new Vector3(0,.04f,0);
            paper.hitBox.size = new Vector3(dimensions.x,.10f,dimensions.z);
            paper.body.position = ConstrainPaper(paper,position);
            Box(paper.transform,"Paper thickness",Vector3.zero,new Vector3(dimensions.x*.985f,dimensions.y,dimensions.z*.985f),paperSide);
            float x = dimensions.x*.5f, z = dimensions.z*.5f, fold = dimensions.z*(variant % 2 == 0 ? -.08f : .01f);
            Vector3 a = new Vector3(-x,.018f,-z), b = new Vector3(-x,.022f,z), c = new Vector3(x,.022f,z), d = new Vector3(x,.018f,-z);
            Vector3 middle = new Vector3(0,.029f,fold);
            PaperFace(paper.transform,"Left paper fold",new[] { a,b,middle },dimensions);
            PaperFace(paper.transform,"Right paper fold",new[] { middle,c,d },dimensions);
            PaperFace(paper.transform,"Lower paper fold",new[] { a,middle,d },dimensions);
            Vector3 tip = new Vector3(0,.038f,fold);
            PaperFace(paper.transform,"Envelope flap",new[] { b+Vector3.up*.01f,c+Vector3.up*.01f,tip },dimensions,true);
            var seam = new GameObject("Delicate fold line"); seam.transform.SetParent(paper.transform,false);
            var line = seam.AddComponent<LineRenderer>(); line.useWorldSpace = false; line.positionCount = 3;
            line.SetPositions(new[] { b+Vector3.up*.012f,tip+Vector3.up*.002f,c+Vector3.up*.012f });
            line.widthMultiplier = .009f; line.sharedMaterial = crease; line.shadowCastingMode = ShadowCastingMode.Off;
            BuildWaxSeal(paper.transform,new Vector3(0,.042f,fold));
            EnvelopeDecoration(paper.transform,x,z,fold);
            Papers.Add(paper);
        }

        void PaperFace(Transform parent, string name, Vector3[] points, Vector3 size, bool flap = false)
        {
            // Sample only the blank paper grain; the envelope's folds and silhouette are actual geometry.
            Rect uv = new Rect(.12f,.12f,.76f,.76f);
            var mesh = new Mesh { name = name, vertices = points, triangles = new[] { 0,1,2 } };
            var tex = new Vector2[3];
            for (int i = 0; i < 3; i++) tex[i] = new Vector2(uv.x+(points[i].x/size.x+.5f)*uv.width,uv.y+(points[i].z/size.z+.5f)*uv.height);
            mesh.uv = tex; mesh.RecalculateNormals(); mesh.RecalculateTangents(); generated.Add(mesh); MeshObject(parent,name,mesh,flap ? paperTop : paperFold);
        }

        void BuildWand(Transform parent)
        {
            var vertices = new Vector3[34]; var uv = new Vector2[34]; var triangles = new List<int>();
            float[] z = { -1.55f,-1.1f,.8f,1.55f }; float[] radius = { .14f,.105f,.055f,.025f };
            for (int ring = 0; ring < 4; ring++) for (int side = 0; side < 8; side++)
            {
                float angle = side * Mathf.PI / 4; vertices[ring*8+side] = new Vector3(Mathf.Cos(angle)*radius[ring],Mathf.Sin(angle)*radius[ring],z[ring]);
                uv[ring*8+side] = new Vector2(ring/3f,side/8f*.15f);
                if (ring == 3) continue;
                int a = ring*8+side, b = ring*8+(side+1)%8;
                triangles.AddRange(new[] { a,b,a+8,b,b+8,a+8 });
            }
            vertices[32] = new Vector3(0,0,z[0]); vertices[33] = new Vector3(0,0,z[3]);
            for (int i = 0; i < 8; i++) triangles.AddRange(new[] { 32,(i+1)%8,i,33,24+i,24+(i+1)%8 });
            var mesh = new Mesh { name = "Tapered wooden wand", vertices = vertices, uv = uv, triangles = triangles.ToArray() }; mesh.RecalculateNormals(); generated.Add(mesh);
            MeshObject(parent,"Wooden wand",mesh,wood);
            for (int i = 0; i < 2; i++)
            {
                GameObject band = Cylinder(parent,"Olive handle binding",new Vector3(0,0,-.96f+i*.23f),.225f,.075f,sage);
                band.transform.localRotation = Quaternion.Euler(90,0,0);
            }
        }

        GameObject Box(Transform parent, string name, Vector3 position, Vector3 size, Material material)
        {
            GameObject obj = GameObject.CreatePrimitive(PrimitiveType.Cube); obj.name = name; obj.transform.SetParent(parent,false);
            obj.transform.localPosition = position; obj.transform.localScale = size;
            Destroy(obj.GetComponent<Collider>()); obj.GetComponent<Renderer>().sharedMaterial = material; return obj;
        }

        GameObject Cylinder(Transform parent, string name, Vector3 position, float diameter, float height, Material material)
        {
            GameObject obj = GameObject.CreatePrimitive(PrimitiveType.Cylinder); obj.name = name; obj.transform.SetParent(parent,false);
            obj.transform.localPosition = position; obj.transform.localScale = new Vector3(diameter,height*.5f,diameter);
            Destroy(obj.GetComponent<Collider>()); obj.GetComponent<Renderer>().sharedMaterial = material; return obj;
        }

        Rect AtlasUv(int crop)
        {
            Rect r = Crops[crop]; return new Rect(r.x/1760f,1-(r.y+r.height)/880f,r.width/1760f,r.height/880f);
        }

        void Surface(Transform parent, string name, Vector3 position, Vector2 size, Material material, Rect uv)
        {
            var mesh = new Mesh { name = name };
            float x = size.x*.5f, z = size.y*.5f;
            mesh.vertices = new[] { new Vector3(-x,0,-z),new Vector3(-x,0,z),new Vector3(x,0,z),new Vector3(x,0,-z) };
            mesh.uv = new[] { new Vector2(uv.xMin,uv.yMin),new Vector2(uv.xMin,uv.yMax),new Vector2(uv.xMax,uv.yMax),new Vector2(uv.xMax,uv.yMin) };
            mesh.triangles = new[] { 0,1,2,0,2,3 }; mesh.RecalculateNormals(); generated.Add(mesh);
            GameObject obj = MeshObject(parent,name,mesh,material); obj.transform.localPosition = position;
        }

        GameObject MeshObject(Transform parent, string name, Mesh mesh, Material material)
        {
            var obj = new GameObject(name); obj.transform.SetParent(parent,false);
            obj.AddComponent<MeshFilter>().sharedMesh = mesh; obj.AddComponent<MeshRenderer>().sharedMaterial = material; return obj;
        }

        void BuildInterface()
        {
            var ui = new GameObject("P2 light paper UI",typeof(RectTransform)); ui.transform.SetParent(transform);
            canvas = ui.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 20;
            Rect uv = AtlasUv(7);
            var sprite = Sprite.Create(atlas,new Rect(uv.x*atlas.width,uv.y*atlas.height,uv.width*atlas.width,uv.height*atlas.height),new Vector2(.5f,.5f),100);
            generated.Add(sprite);
            dialog = UiRect("Item introduction",ui.transform,new Vector2(0,-329),new Vector2(555,164));
            dialog.gameObject.AddComponent<Image>().sprite = sprite;
            title = Label(dialog,"Title",new Vector2(-5,44),new Vector2(450,40),31,TextAnchor.MiddleLeft);
            description = Label(dialog,"Description",new Vector2(-5,-17),new Vector2(450,72),23,TextAnchor.UpperLeft);
            description.lineSpacing = 1.2f;
            closeTarget = UiRect("Close",dialog,new Vector2(230,43),new Vector2(42,42));
            Stroke(closeTarget,Vector2.zero,new Vector2(22,2.5f),45,new Color(.57f,.61f,.43f));
            Stroke(closeTarget,Vector2.zero,new Vector2(22,2.5f),-45,new Color(.57f,.61f,.43f));
            dialog.gameObject.SetActive(false);
            hint = UiRect("Long press gesture",ui.transform,new Vector2(0,-393),new Vector2(76,91));
            // Use the same mouse drawing and stroke weight as LifeGestureBook's practice hint.
            var mouse = hint.gameObject.AddComponent<WeatherHousePrototype.AnimalGlyphGraphic>();
            mouse.SvgBody = "<path d=\"M 35 43 Q 35 25 50 25 Q 65 25 65 43 L 65 76 Q 65 93 50 93 Q 35 93 35 76 L 35 43 M 50 26 L 50 53 M 36 53 L 64 53 M 41 36 L 44 36 L 44 45 L 41 45 L 41 36\"/>";
            mouse.lineWidth = 3.8f; mouse.color = new Color(.94f,.94f,.82f,.9f); mouse.raycastTarget = false;
            completePanel = UiRect("Ready to leave",ui.transform,new Vector2(0,-360),new Vector2(390,72));
            completePanel.gameObject.AddComponent<Image>().sprite = sprite;
            Label(completePanel,"Ready",Vector2.zero,new Vector2(340,60),25,TextAnchor.MiddleCenter).text = "东西都带齐了，准备出发。";
            completePanel.gameObject.SetActive(false);
        }

        RectTransform UiRect(string name, Transform parent, Vector2 position, Vector2 size)
        {
            var obj = new GameObject(name,typeof(RectTransform)); var rect = obj.GetComponent<RectTransform>(); rect.SetParent(parent,false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f,.5f); rect.anchoredPosition = position; rect.sizeDelta = size; return rect;
        }

        Text Label(Transform parent, string name, Vector2 position, Vector2 size, int fontSize, TextAnchor alignment)
        {
            var rect = UiRect(name,parent,position,size); var text = rect.gameObject.AddComponent<Text>();
            text.font = font; text.fontSize = fontSize; text.color = new Color(.26f,.30f,.23f); text.alignment = alignment; text.raycastTarget = false; return text;
        }

        void Stroke(Transform parent, Vector2 position, Vector2 size, float angle, Color color)
        {
            RectTransform rect = UiRect("Stroke",parent,position,size); rect.localRotation = Quaternion.Euler(0,0,angle);
            var image = rect.gameObject.AddComponent<Image>(); image.color = color; image.raycastTarget = false;
        }

        void BuildMagic()
        {
            magic = new GameObject("Pickup magic"); magic.transform.SetParent(transform);
            var ring = new GameObject("Fine light circle"); ring.transform.SetParent(magic.transform,false);
            var line = ring.AddComponent<LineRenderer>(); line.useWorldSpace = false; line.loop = true; line.positionCount = 64;
            line.widthMultiplier = .012f; line.sharedMaterial = glowMaterial; line.shadowCastingMode = ShadowCastingMode.Off;
            for (int i = 0; i < 64; i++) line.SetPosition(i,new Vector3(Mathf.Cos(i*Mathf.PI/32)*1.7f,0,Mathf.Sin(i*Mathf.PI/32)*1.7f));
            for (int i = 0; i < 3; i++)
            {
                var mesh = new Mesh { name = "Small pickup sparkle" };
                mesh.vertices = new[] { new Vector3(-.08f,0,0),new Vector3(0,0,.18f),new Vector3(.08f,0,0),new Vector3(0,0,-.18f) };
                mesh.triangles = new[] { 0,1,2,0,2,3 }; mesh.RecalculateNormals(); generated.Add(mesh);
                var star = MeshObject(magic.transform,"Sparkle",mesh,glowMaterial);
                star.transform.localPosition = new Vector3(i == 1 ? 1.6f : -1.5f,.2f,i == 0 ? 1.1f : i == 1 ? .5f : -.9f); stars.Add(star.transform);
            }
            magic.SetActive(false);
        }

        void Update()
        {
            if (!ViewCamera || !canvas) return;
            ApplyLighting();
            float scale = Mathf.Min(Screen.width/1600f,Screen.height/900f); canvas.scaleFactor = scale;
            float width = 1600*scale/Screen.width, height = 900*scale/Screen.height;
            ViewCamera.rect = new Rect((1-width)*.5f,(1-height)*.5f,width,height);
            if (!Automation)
            {
                Pointer(Input.mousePosition,Input.GetMouseButtonDown(0),Input.GetMouseButton(0),Input.GetMouseButtonUp(0));
                if (Input.GetKeyDown(KeyCode.Escape) && selected && !busy) CloseItem();
            }
            for (int i = 0; i < stars.Count; i++) stars[i].localScale = Vector3.one*(.82f+.15f*Mathf.Sin(Time.unscaledTime*3+i*2));
            if (PaperOutline && PaperOutline.gameObject.activeSelf)
                paperHighlight.SetColor("_Color",new Color(1.4f,1.05f,.32f,.55f+.25f*Mathf.Sin(Time.unscaledTime*4)));
        }

        public void Pointer(Vector2 screen, bool down, bool held, bool up)
        {
            if (busy) return;
            if (selected)
            {
                if (down && RectTransformUtility.RectangleContainsScreenPoint(closeTarget,screen)) CloseItem();
                return;
            }
            if (down)
            {
                pressed = Hit(screen); pressScreen = screen; pressTime = Time.unscaledTime;
                if (pressed && pressed.IsPaper) dragOffset = pressed.transform.position - FloorPoint(screen,pressed.transform.position.y);
                ShowPaperFeedback(pressed && pressed.IsPaper ? pressed : null);
            }
            if (held && pressed && pressed.IsPaper && Time.unscaledTime-pressTime >= .12f)
            {
                if (!dragging)
                {
                    dragging = pressed; dragHeight = Mathf.Clamp(dragging.body.position.y+.035f,.145f,1.2f);
                    dragRotation = Quaternion.Euler(0,dragging.body.rotation.eulerAngles.y,0);
                    dragging.body.velocity = Vector3.zero; dragging.body.angularVelocity = Vector3.zero;
                    dragging.body.useGravity = false;
                    dragging.body.isKinematic = true;
                    if (paperSound) sound.PlayOneShot(paperSound,.65f); hint.gameObject.SetActive(false);
                }
                dragTarget = FloorPoint(screen,dragHeight)+new Vector3(dragOffset.x,0,dragOffset.z);
                dragTarget = ConstrainPaper(dragging,dragTarget); dragTarget.y = dragHeight;
            }
            if (up)
            {
                if (dragging) ReleasePaper();
                else if (pressed && !pressed.IsPaper && Hit(screen) == pressed && Vector2.Distance(pressScreen,screen) < 20 && CanPickup(pressed)) OpenItem(pressed);
                pressed = null;
                ShowPaperFeedback(null);
            }
        }

        void FixedUpdate()
        {
            // A held letter follows the hand; kinematic contact still pushes the surrounding dynamic letters.
            if (dragging)
            {
                dragging.body.MovePosition(dragTarget);
                dragging.body.MoveRotation(Quaternion.Slerp(dragging.body.rotation,dragRotation,.35f));
            }
            foreach (DrawerToken paper in Papers)
            {
                // A perimeter safety constraint also contains paper lifted above a stack or hit at high speed.
                Vector3 constrained = ConstrainPaper(paper,paper.body.position);
                if ((constrained-paper.body.position).sqrMagnitude < .000001f) continue;
                Vector3 velocity = paper.body.velocity;
                if (Mathf.Abs(constrained.x-paper.body.position.x) > .001f) velocity.x = 0;
                if (Mathf.Abs(constrained.z-paper.body.position.z) > .001f) velocity.z = 0;
                paper.body.position = constrained; paper.body.velocity = velocity;
            }
        }

        void ReleasePaper()
        {
            Vector3 releaseVelocity = dragging.body.velocity;
            dragging.body.isKinematic = false; dragging.body.useGravity = true; dragging.body.constraints = RigidbodyConstraints.None;
            dragging.body.velocity = Vector3.ClampMagnitude(releaseVelocity,1.2f); dragging = null;
            if (paperSound) sound.PlayOneShot(paperSound,.25f);
        }

        void OnApplicationFocus(bool focus) { if (!focus) { if (dragging) ReleasePaper(); pressed = null; ShowPaperFeedback(null); } }

        public DrawerToken Hit(Vector2 screen)
        {
            if (!ViewCamera.pixelRect.Contains(screen)) return null;
            return Physics.Raycast(ViewCamera.ScreenPointToRay(screen),out RaycastHit hit,50) ? hit.collider.GetComponentInParent<DrawerToken>() : null;
        }

        Vector3 FloorPoint(Vector2 screen, float height)
        {
            Ray ray = ViewCamera.ScreenPointToRay(screen); var plane = new Plane(Vector3.up,new Vector3(0,height,0));
            return plane.Raycast(ray,out float distance) ? ray.GetPoint(distance) : Vector3.zero;
        }

        public bool CanPickup(DrawerToken item)
        {
            if (item.docked) return true;
            int visible = 0;
            for (int x = -1; x <= 1; x++) for (int z = -1; z <= 1; z++)
            {
                Vector3 point = item.transform.TransformPoint(new Vector3(item.dimensions.x*x*.27f,0,item.dimensions.z*z*.27f));
                if (TopAt(point) == item) visible++;
            }
            return visible >= 3;
        }

        public DrawerToken TopAt(Vector3 point)
        {
            return Hit(ViewCamera.WorldToScreenPoint(point));
        }

        public Vector3 VisiblePoint(DrawerToken item)
        {
            int steps = item.IsPaper ? 5 : 1;
            float spacing = item.IsPaper ? .09f : .27f;
            for (int x = -steps; x <= steps; x++) for (int z = -steps; z <= steps; z++)
            {
                Vector3 point = item.transform.TransformPoint(new Vector3(item.dimensions.x*x*spacing,0,item.dimensions.z*z*spacing));
                if (TopAt(point) == item) return point;
            }
            return item.transform.position;
        }

        void OpenItem(DrawerToken item)
        {
            if (busy || selected) return;
            selected = item; busy = true; Progress.collected[item.item] = true; Save();
            item.body.velocity = Vector3.zero; item.body.angularVelocity = Vector3.zero; item.body.isKinematic = true; item.hitBox.enabled = false;
            hint.gameObject.SetActive(false); completePanel.gameObject.SetActive(false);
            if (pickupSound) sound.PlayOneShot(pickupSound);
            StartCoroutine(Present(item));
        }

        IEnumerator Present(DrawerToken item)
        {
            Vector3 target = DrawerCenter+new Vector3(0,2.0f,.42f);
            magic.transform.position = new Vector3(target.x,1.4f,target.z); magic.SetActive(true);
            float factor = item.item == 3 ? 1.05f : item.item == 2 ? 1.55f : 1.5f;
            yield return Tween(item,target,Quaternion.Euler(-28,-10,7),Vector3.one*factor,.38f);
            title.text = Titles[item.item]; description.text = item.item == 4 && letterContent ? letterContent.message : Descriptions[item.item];
            float textHeight = item.item == 4 ? Mathf.Max(72,description.preferredHeight) : 72;
            float panelHeight = textHeight+92;
            dialog.sizeDelta = new Vector2(555,panelHeight); dialog.anchoredPosition = new Vector2(0,-411+panelHeight*.5f);
            title.rectTransform.anchoredPosition = new Vector2(-5,panelHeight*.5f-38);
            closeTarget.anchoredPosition = new Vector2(230,panelHeight*.5f-39);
            description.rectTransform.sizeDelta = new Vector2(450,textHeight);
            description.rectTransform.anchoredPosition = new Vector2(-5,-17);
            dialog.gameObject.SetActive(true); busy = false;
        }

        public void CloseItem()
        {
            if (!selected || busy) return;
            busy = true; dialog.gameObject.SetActive(false); magic.SetActive(false);
            Progress.read[selected.item] = true; Save(); StartCoroutine(ReturnToShelf(selected));
        }

        IEnumerator ReturnToShelf(DrawerToken item)
        {
            yield return Tween(item,Shelf(item.item),Quaternion.identity,Vector3.one*ShelfScale(item.item),.34f);
            item.docked = true; item.hitBox.enabled = true; selected = null; busy = false; RefreshCompletion();
        }

        IEnumerator Tween(DrawerToken item, Vector3 position, Quaternion rotation, Vector3 scale, float duration)
        {
            Vector3 from = item.transform.position, size = item.transform.localScale; Quaternion angle = item.transform.rotation;
            for (float time = 0; time < duration; time += Time.unscaledDeltaTime)
            {
                float t = Mathf.SmoothStep(0,1,time/duration);
                item.transform.SetPositionAndRotation(Vector3.Lerp(from,position,t),Quaternion.Slerp(angle,rotation,t));
                item.transform.localScale = Vector3.Lerp(size,scale,t); yield return null;
            }
            item.transform.SetPositionAndRotation(position,rotation); item.transform.localScale = scale;
        }

        Vector3 Shelf(int index) => new Vector3(6.35f,.32f,3.0f-index*1.34f);
        float ShelfScale(int index) => index == 3 ? .37f : index == 2 ? .59f : .51f;

        void DockImmediately(DrawerToken item)
        {
            item.docked = true; item.body.isKinematic = true; item.transform.SetPositionAndRotation(Shelf(item.item),Quaternion.identity);
            item.transform.localScale = Vector3.one*ShelfScale(item.item);
        }

        void RefreshCompletion() { if (completePanel) completePanel.gameObject.SetActive(Progress.Complete && !selected); }

        void Save()
        {
            if (readOnly) return;
            try { Directory.CreateDirectory(Path.GetDirectoryName(SavePath)); File.WriteAllText(SavePath,JsonUtility.ToJson(Progress,true)); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            { SaveError = e.Message; readOnly = true; Debug.LogWarning("Drawer prototype progress could not be saved: " + e.Message); }
        }

        void OnDestroy() { foreach (var item in generated) if (item) Destroy(item); }
    }
}
