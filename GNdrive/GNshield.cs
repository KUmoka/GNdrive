using KSP;
using KSP.IO;
using System;
using System.Linq;
using UnityEngine;

namespace GNTechnology
{
    public static class GNParticleHelpers
    {
        /// <summary>
        /// パーツに「球の表面にランダム分布する粒子」を出す Shuriken ParticleSystem を追加して返す。
        /// 細かい動きや色の制御は、呼び出し元で ps に対して追加設定してOK。
        /// </summary>
        /// <param name="part">エミッタをぶら下げる Part</param>
        /// <param name="name">GameObject 名（デフォルト: "GN_SphereEmitter"）</param>
        /// <param name="radius">球の半径（メートル）</param>
        /// <param name="rateOverTime">毎秒の発生粒子数</param>
        /// <param name="startSize">開始サイズ</param>
        /// <param name="startLifetime">粒子寿命（秒）</param>
        /// <param name="worldSpace">
        /// true: World空間でシミュレーション（船の動きと独立して“場”が見える）  
        /// false: Partローカル空間でシミュレーション（パーツにくっついて動く）
        /// </param>
        public static ParticleSystem CreateSphericalShellEmitter(
            Part part,
            string name = "GN_SphereEmitter",
            float radius = 0f,
            float rateOverTime = 0f,
            float startSize = 0f,
            float startLifetime = 0f,
            float startSpeed = 0f,
            float thickness = 0f,
            bool worldSpace = false // local space by default
        )
        {
            if (part == null)
            {
                Debug.LogError("[GN] CreateSphericalShellEmitter: part is null");
                return null;
            }

            // パーツの子に GameObject を生成
            var go = new GameObject(name);
            go.transform.SetParent(part.transform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;

            // Shuriken ParticleSystem を追加
            var ps = go.AddComponent<ParticleSystem>();

            // === main モジュール設定 ===
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = true;
            main.startLifetime = startLifetime;
            main.startSpeed = startSpeed;          // とりあえず球面上に“貼り付ける”
            main.startSize = startSize;
            main.maxParticles = 10000;

            main.simulationSpace = worldSpace
                ? ParticleSystemSimulationSpace.World
                : ParticleSystemSimulationSpace.Local;

            // 色はとりあえず白にしておく（後で外から変えてOK）
            main.startColor = Color.white;

            // === emission モジュール ===
            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = rateOverTime;

            // === shape モジュール：球の表面 ===
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = radius;
            shape.radiusThickness = thickness;   // 0 = 殻（表面）だけ、1 = 球の中身全部

            // === renderer（最低限） ===
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            // TODO: 必要ならここで Shader / Material をセットする

            // === noise ===
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.15f;
            noise.frequency = 0.35f;
            noise.scrollSpeed = 0.25f;

            // ここでは再生しない。呼び出し側で ps.Play() してね！
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            Debug.Log("[GN] Spherical shell emitter created on part: " + part.partInfo?.name);

            return ps;
        }

        public static void SetParticleTexture(ParticleSystem ps, string texturePathRelative)
        {
            if (!ps)
            {
                Debug.LogError("[GN] SetParticleTexture: ParticleSystem is null");
                Debug.LogError("[GN] Texture path: " + texturePathRelative);
                return;
            }

            // 1. 同一パーツフォルダ内にあるファイルをロード
            //    e.g. "MyMod/Parts/GNDrive/particle1" （".png"は不要）
            Texture2D tex = GameDatabase.Instance.GetTexture(texturePathRelative, false);
            if (!tex)
            {
                Debug.LogError("[GN] Failed to load particle texture: " + texturePathRelative);
                return;
            }

            // 2. マテリアル作成（Unity Standard Particle Shader）
            var shader = Shader.Find("KSP/Particles/Additive");
            if (!shader)
            {
                Debug.LogError("[GN] Shader not found!");
                return;
            }

            Material mat = new Material(shader);
            mat.SetTexture("_MainTex", tex);

            if (!mat)
            {
                Debug.LogError("[GN] Failed to create material for particle texture.");
                return;
            }

            // 3. ParticleSystemRenderer に割り当てる
            var rend = ps.GetComponent<ParticleSystemRenderer>();
            rend.material = mat;

            // Billboard（看板型）にするならここ
            rend.renderMode = ParticleSystemRenderMode.Billboard;

            Debug.Log("[GN] Texture applied: " + texturePathRelative);
        }

        public static void SetParticleStartColor(ParticleSystem ps, Color color)
        {
            if (!ps)
            {
                Debug.LogError("[GN] SetParticleTexture: ParticleSystem is null");
                return;
            }
            var main = ps.main;
            main.startColor = color;
            Debug.Log("[GN] Particle start color set to: " + color.ToString());
        }

        public static void SetPSPosition(ParticleSystem ps, Part part, Vessel vessel)
        {
            if (!ps)
            {
                Debug.LogError("[GN] SetPSPosition: ParticleSystem is null");
                return;
            }
            ps.transform.localPosition = part.transform.InverseTransformPoint(vessel.CoM);
            Debug.Log("[GN] ParticleSystem position set to: " + ps.transform.position.ToString());
        }

        public static float GetMaxVesselRadiusFromCoM(Vessel vessel)
        {
            if (vessel == null || !vessel.loaded)
            {
                return 0f;
            }

            // 1. ワールド座標系での重心（CoM）を取得
            Vector3 worldCoM = vessel.CoM;
            float maxDistance = 0f;

            // 2. すべての搭載済みパーツをループ処理
            foreach (Part p in vessel.parts)
            {
                if (p == null) continue;

                // 3. Partのすべてのレンダラーを取得
                // 複数のレンダラーを持つパーツもあるため、GetComponentsInChildrenを使用
                MeshRenderer[] renderers = p.gameObject.GetComponentsInChildren<MeshRenderer>();

                foreach (MeshRenderer renderer in renderers)
                {
                    if (renderer == null || !renderer.enabled) continue;

                    // 4. レンダラーのワールド座標系におけるバウンディングボックスを取得
                    // Renderer.bounds はすでにワールド座標系です。
                    Bounds bounds = renderer.bounds;

                    // 5. バウンディングボックスの8つの角を計算し、CoMとの距離を比較

                    // バウンディングボックスの中心
                    Vector3 center = bounds.center;
                    // バウンディングボックスのサイズ（中心から端までの距離）
                    Vector3 extents = bounds.extents;

                    // バウンディングボックスの8つの角をチェック
                    for (int i = 0; i < 8; i++)
                    {
                        // 各軸の符号を決定 (-1 または +1)
                        float xSign = (i & 1) == 0 ? 1f : -1f;
                        float ySign = (i & 2) == 0 ? 1f : -1f;
                        float zSign = (i & 4) == 0 ? 1f : -1f;

                        // 角のワールド座標を計算
                        Vector3 corner = new Vector3(
                            center.x + extents.x * xSign,
                            center.y + extents.y * ySign,
                            center.z + extents.z * zSign
                        );

                        // CoMからの距離を計算
                        float distance = Vector3.Distance(worldCoM, corner);

                        // 最大距離を更新
                        if (distance > maxDistance)
                        {
                            maxDistance = distance;
                        }
                    }
                }
            }

            return maxDistance;
        }
    }

    public class GNTestSphereFX : PartModule
    {
        private ParticleSystem spherePs;

        [KSPField(guiActive = true, guiActiveEditor = true, guiName = "Radious", isPersistant = true), UI_FloatRange(minValue = 0f, maxValue = 100f, stepIncrement = 0.1f)]
        public float myradius = 1f;
        [KSPField(guiActive = true, guiActiveEditor = true, guiName = "Rate Over Time", isPersistant = true), UI_FloatRange(minValue = 0f, maxValue = 10000f, stepIncrement = 10f)]
        public float myRateOverTime = 10000f;
        [KSPField(guiActive = true, guiActiveEditor = true, guiName = "Start Size", isPersistant = true), UI_FloatRange(minValue = 0f, maxValue = 4f, stepIncrement = 0.1f)]
        public float myStartSize = 2f;
        [KSPField(guiActive = true, guiActiveEditor = true, guiName = "Start Life Time", isPersistant = true), UI_FloatRange(minValue = 0f, maxValue = 10f, stepIncrement = 0.1f)]
        public float myStartLifeTime = 0.4f;
        [KSPField(guiActive = true, guiActiveEditor = true, guiName = "Start Speed", isPersistant = true), UI_FloatRange(minValue = -1f, maxValue = 1f, stepIncrement = 0.1f)]
        public float myStartSpeed = 0f;
        [KSPField(guiActive = true, guiActiveEditor = true, guiName = "Shell Thickness", isPersistant = true), UI_FloatRange(minValue = 0f, maxValue = 1f, stepIncrement = 0.1f)]
        public float myThickness = 0.1f;
        [KSPField(guiActive = true, guiActiveEditor = true, guiName = "GNField", isPersistant = true), UI_Toggle(disabledText = "OFF", enabledText = "ON")]
        public bool FieldON = false;
        [KSPField(guiActive = false, guiName = "TexturePath")]
        public string texPath = "";

        public override void OnStart(StartState state)
        {
            base.OnStart(state);

            // radious initialization
            myradius = GNParticleHelpers.GetMaxVesselRadiusFromCoM(part.vessel) + 1f; // +1mの余裕を持たせる

            // とりあえず半径3m、毎秒200粒子くらいで試す
            spherePs = GNTechnology.GNParticleHelpers.CreateSphericalShellEmitter(
                part,
                name: "GN_TestSphereEmitter",
                radius: myradius,
                rateOverTime: myRateOverTime,
                startSize: myradius * 0.01f * myStartSize,
                startLifetime: myStartLifeTime,
                startSpeed: myStartSpeed,
                thickness: myradius,
                worldSpace: true   // とりあえずWorld空間で様子を見る
            );

            if(texPath == "")
            {
                Debug.LogWarning("[GN] Texture path is empty.");
                return;
            }

            // テクスチャも設定してみる
            GNParticleHelpers.SetParticleTexture(spherePs, texPath);
            GNParticleHelpers.SetParticleStartColor(spherePs, new Color(0f, 1f, 170f / 255f, 1f));
            GNParticleHelpers.SetPSPosition(spherePs, part, part.vessel);
        }

        public override void OnUpdate()
        {
            base.OnUpdate();
            // パラメータ変更に追従させる
            if (spherePs != null)
            {
                var main = spherePs.main;
                main.startSize = myradius * 0.01f * myStartSize;
                main.startLifetime = myStartLifeTime;
                main.startSpeed = myStartSpeed;
                var emission = spherePs.emission;
                emission.rateOverTime = myRateOverTime;
                var shape = spherePs.shape;
                shape.radius = myradius;
                shape.radiusThickness = myThickness;

                if (FieldON)
                {
                    if (!spherePs.isPlaying)
                    {
                        spherePs.Play();
                    }
                }
                else
                {
                    if (spherePs.isPlaying)
                    {
                        spherePs.Stop();
                    }
                }

                GNParticleHelpers.SetPSPosition(spherePs, part, part.vessel);
            }
        }

        public override void OnInactive()
        {
            base.OnInactive();
            // 片付け（お好みで）
            if (spherePs != null)
            {
                spherePs.Stop();
            }
        }
    }
}
