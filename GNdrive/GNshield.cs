using KSP;
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
            float radius = 2f,
            float rateOverTime = 100f,
            float startSize = 0.25f,
            float startLifetime = 1.5f,
            float startSpeed = 0.1f,
            bool worldSpace = true
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
            shape.radiusThickness = 0f;   // 0 = 殻（表面）だけ、1 = 球の中身全部

            // === renderer（最低限） ===
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            // TODO: 必要ならここで Shader / Material をセットする

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
    }

    public class GNTestSphereFX : PartModule
    {
        private ParticleSystem spherePs;

        [KSPField(guiActive = true, guiActiveEditor = true, guiName = "Radious", isPersistant = true), UI_FloatRange(minValue = 0f, maxValue = 10f, stepIncrement = 0.1f)]
        public float myradius = 10f;
        [KSPField(guiActive = true, guiActiveEditor = true, guiName = "Rate Over Time", isPersistant = true), UI_FloatRange(minValue = 0f, maxValue = 5000f, stepIncrement = 10f)]
        public float myRateOverTime = 5000f;
        [KSPField(guiActive = true, guiActiveEditor = true, guiName = "Start Size", isPersistant = true), UI_FloatRange(minValue = 0f, maxValue = 1f, stepIncrement = 0.1f)]
        public float myStartSize = 0.2f;
        [KSPField(guiActive = true, guiActiveEditor = true, guiName = "Start Life Time", isPersistant = true), UI_FloatRange(minValue = 0f, maxValue = 10f, stepIncrement = 0.1f)]
        public float myStartLifeTime = 0.4f;
        [KSPField(guiActive = true, guiActiveEditor = true, guiName = "Start Speed", isPersistant = true), UI_FloatRange(minValue = -1f, maxValue = 1f, stepIncrement = 0.1f)]
        public float myStartSpeed = -0.1f;
        [KSPField(guiActive = true, guiActiveEditor = true, guiName = "GNField", isPersistant = true), UI_Toggle(disabledText = "OFF", enabledText = "ON")]
        public bool FieldON = false;
        [KSPField(guiActive = false, guiName = "TexturePath")]
        public string texPath = "";

        public override void OnStart(StartState state)
        {
            base.OnStart(state);

            // とりあえず半径3m、毎秒200粒子くらいで試す
            spherePs = GNTechnology.GNParticleHelpers.CreateSphericalShellEmitter(
                part,
                name: "GN_TestSphereEmitter",
                radius: myradius,
                rateOverTime: myRateOverTime,
                startSize: myStartSize,
                startLifetime: myStartLifeTime,
                startSpeed: myStartSpeed,
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

        }

        public override void OnUpdate()
        {
            base.OnUpdate();
            // パラメータ変更に追従させる
            if (spherePs != null)
            {
                var main = spherePs.main;
                main.startSize = myStartSize;
                main.startLifetime = myStartLifeTime;
                main.startSpeed = myStartSpeed;
                var emission = spherePs.emission;
                emission.rateOverTime = myRateOverTime;
                var shape = spherePs.shape;
                shape.radius = myradius;

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
