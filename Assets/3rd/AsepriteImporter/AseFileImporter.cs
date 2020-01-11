using System.Collections.Generic;
using UnityEngine;
using UnityEditor.Experimental.AssetImporters;
using System.IO;
using Aseprite;
using UnityEditor;
using Aseprite.Chunks;
using System.Text;

namespace AsepriteImporter
{
    public enum AseFileImportType
    {
        Sprite,
        Tileset,
        LayerToSprite
    }

    [ScriptedImporter(1, new[] { "ase", "aseprite" })]
    public class AseFileImporter : ScriptedImporter
    {
        [SerializeField] public AseFileTextureSettings textureSettings = new AseFileTextureSettings();
        [SerializeField] public AseFileAnimationSettings[] animationSettings;
        [SerializeField] public Texture2D atlas;
        [SerializeField] public AseFileImportType importType;

        public AseFile aseFile;

        public override void OnImportAsset(AssetImportContext ctx)
        {
            name = GetFileName(ctx.assetPath);

            AseFile aseFile = ReadAseFile(ctx.assetPath);

            //Debug.Log(string.Format("导入Ase动画文件：{0}，帧间隔为{1}（相当于动画帧率{2})", name, aseFile.Header.Speed, Mathf.RoundToInt(1000 / aseFile.Header.Speed)));

            this.aseFile = aseFile;

            SpriteAtlasBuilder atlasBuilder = new SpriteAtlasBuilder(textureSettings, aseFile.Header.Width, aseFile.Header.Height);

            Texture2D[] frames;
            if (importType != AseFileImportType.LayerToSprite)
                frames = aseFile.GetFrames();
            else
                frames = aseFile.GetLayersAsFrames();

            SpriteImportData[] spriteImportData = new SpriteImportData[0];

            //翻转贴图
            if (textureSettings.flipTexture)
            {
                for (int i = 0; i < frames.Length; i++)
                {
                    frames[i] = FlipTexture(frames[i]);
                }
            }

            atlas = atlasBuilder.GenerateAtlas(frames, out spriteImportData, textureSettings.transparentMask, false);

            atlas.filterMode = textureSettings.filterMode;
            atlas.alphaIsTransparency = false;
            atlas.wrapMode = TextureWrapMode.Clamp;
            atlas.name = "Texture";

            ctx.AddObjectToAsset("Texture", atlas);

            ctx.SetMainObject(atlas);

            switch (importType)
            {
                case AseFileImportType.LayerToSprite:
                case AseFileImportType.Sprite:
                    ImportSprites(ctx, aseFile, spriteImportData);
                    break;
                case AseFileImportType.Tileset:
                    ImportTileset(ctx, atlas);
                    break;
            }

            ctx.SetMainObject(atlas);
        }

        private void ImportSprites(AssetImportContext ctx, AseFile aseFile, SpriteImportData[] spriteImportData)
        {
            int spriteCount = spriteImportData.Length;

            Sprite[] sprites = new Sprite[spriteCount];

            for (int i = 0; i < spriteCount; i++)
            {
                Sprite sprite = Sprite.Create(atlas,
                    spriteImportData[i].rect,
                    spriteImportData[i].pivot, textureSettings.pixelsPerUnit, textureSettings.extrudeEdges,
                    textureSettings.meshType, spriteImportData[i].border, textureSettings.generatePhysics);

                sprite.name = string.Format("{0}_{1}", name, spriteImportData[i].name);

                ctx.AddObjectToAsset(sprite.name, sprite);

                //AssetDatabase.CreateAsset(sprite, GetFolderPath(ctx.assetPath) + "/" + sprite.name + ".asset");
                //AssetDatabase.Refresh();

                sprites[i] = sprite;
            }

            //如果有Tag
            if (aseFile.GetAnimations().Length > 0)
            {
                GenerateAnimations(ctx, aseFile, sprites);
            }
            else
            {
                GenerateAnimation(ctx, aseFile, sprites);
            }
        }

        Texture2D SpriteToTexture(Sprite sprite)
        {
            var croppedTexture = new Texture2D((int)sprite.rect.width, (int)sprite.rect.height);
            var pixels = sprite.texture.GetPixels((int)sprite.textureRect.x,
                                                    (int)sprite.textureRect.y,
                                                    (int)sprite.textureRect.width,
                                                    (int)sprite.textureRect.height);
            croppedTexture.SetPixels(pixels);
            croppedTexture.Apply();

            return croppedTexture;
        }

        //翻转Texture2D
        Texture2D FlipTexture(Texture2D original)
        {
            Texture2D flipped = new Texture2D(original.width, original.height);

            int xN = original.width;
            int yN = original.height;

            for (int i = 0; i < xN; i++)
            {
                for (int j = 0; j < yN; j++)
                {
                    flipped.SetPixel(xN - i - 1, j, original.GetPixel(i, j));
                }
            }
            flipped.Apply();

            return flipped;
        }

        private void ImportTileset(AssetImportContext ctx, Texture2D atlas)
        {
            int cols = atlas.width / textureSettings.tileSize.x;
            int rows = atlas.height / textureSettings.tileSize.y;

            int width = textureSettings.tileSize.x;
            int height = textureSettings.tileSize.y;

            int index = 0;

            for (int y = rows - 1; y >= 0; y--)
            {
                for (int x = 0; x < cols; x++)
                {
                    Rect tileRect = new Rect(x * width, y * height, width, height);

                    Sprite sprite = Sprite.Create(atlas, tileRect, textureSettings.spritePivot,
                        textureSettings.pixelsPerUnit, textureSettings.extrudeEdges, textureSettings.meshType,
                        Vector4.zero, textureSettings.generatePhysics);
                    sprite.name = string.Format("{0}_{1}", name, index);

                    ctx.AddObjectToAsset(sprite.name, sprite);

                    index++;
                }
            }
        }

        private string GetFileName(string assetPath)
        {
            string[] parts = assetPath.Split('/');
            string filename = parts[parts.Length - 1];

            return filename.Substring(0, filename.LastIndexOf('.'));
        }

        string GetFolderPath(string assetPath)
        {
            return assetPath.Substring(0, assetPath.LastIndexOf('/'));
        }

        private static AseFile ReadAseFile(string assetPath)
        {
            FileStream fileStream = new FileStream(assetPath, FileMode.Open, FileAccess.Read);
            AseFile aseFile = new AseFile(fileStream);
            fileStream.Close();

            return aseFile;
        }

        public void GenerateAnimation(AssetImportContext ctx, AseFile aseFile, Sprite[] sprites)
        {
            AnimationClip animationClip = new AnimationClip
            {
                name = name,
                frameRate = 25
            };

            EditorCurveBinding spriteBinding = new EditorCurveBinding();
            spriteBinding.type = typeof(SpriteRenderer);
            spriteBinding.path = "";
            spriteBinding.propertyName = "m_Sprite";

            int length = sprites.Length;
            ObjectReferenceKeyframe[] spriteKeyFrames = new ObjectReferenceKeyframe[length + 1]; // plus last frame to keep the duration

            float time = 0;

            int from = 0;
            int step = 1;

            int keyIndex = from;

            for (int i = 0; i < length; i++)
            {
                if (i >= length)
                {
                    keyIndex = from;
                }


                ObjectReferenceKeyframe frame = new ObjectReferenceKeyframe
                {
                    time = time,
                    value = sprites[keyIndex]
                };

                time += aseFile.Frames[keyIndex].FrameDuration / 1000f;

                keyIndex += step;
                spriteKeyFrames[i] = frame;
            }

            float frameTime = 1f / animationClip.frameRate;

            ObjectReferenceKeyframe lastFrame = new ObjectReferenceKeyframe();
            lastFrame.time = time - frameTime;
            lastFrame.value = sprites[keyIndex - step];

            spriteKeyFrames[spriteKeyFrames.Length - 1] = lastFrame;


            AnimationUtility.SetObjectReferenceCurve(animationClip, spriteBinding, spriteKeyFrames);
            AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(animationClip);

            //switch (animation.Animation)
            //{
            //    case LoopAnimation.Forward:
            //        animationClip.wrapMode = WrapMode.Loop;
            //        settings.loopTime = true;
            //        break;
            //    case LoopAnimation.Reverse:
            //        animationClip.wrapMode = WrapMode.Loop;
            //        settings.loopTime = true;
            //        break;
            //    case LoopAnimation.PingPong:
            //        animationClip.wrapMode = WrapMode.PingPong;
            //        settings.loopTime = true;
            //        break;
            //}

            //if (!importSettings.loopTime)
            //{
            //    animationClip.wrapMode = WrapMode.Once;
            //    settings.loopTime = false;
            //}

            AnimationUtility.SetAnimationClipSettings(animationClip, settings);
            ctx.AddObjectToAsset(name, animationClip);

            //在同一路径创建动画文件
            //AssetDatabase.CreateAsset(animationClip, GetFolderPath(ctx.assetPath) + "/" + name + ".anim");
            //AssetDatabase.Refresh();
        }

        public void GenerateAnimations(AssetImportContext ctx, AseFile aseFile, Sprite[] sprites)
        {
            if (animationSettings == null)
                animationSettings = new AseFileAnimationSettings[0];

            var animSettings = new List<AseFileAnimationSettings>(animationSettings);
            var animations = aseFile.GetAnimations();

            if (animations.Length <= 0)
                return;

            if (animationSettings != null)
                RemoveUnusedAnimationSettings(animSettings, animations);

            int index = 0;

            foreach (var animation in animations)
            {
                AnimationClip animationClip = new AnimationClip();

                //string fileName = string.Format("{0}_{1}", name, animation.TagName);
                //string path = string.Format("{0}/{1}.anim", GetFolderPath(ctx.assetPath), fileName);
                ////animationClip = AssetDatabase.LoadAssetAtPath(path, typeof(Animation));

                //if(AssetDatabase.LoadMainAssetAtPath(path) != null)
                //{
                //    animationClip = AssetDatabase.LoadMainAssetAtPath(path) as AnimationClip;
                //}

                animationClip.name = name + "_" + animation.TagName;
                animationClip.frameRate = 25;

                AseFileAnimationSettings importSettings = GetAnimationSettingFor(animSettings, animation);
                importSettings.about = GetAnimationAbout(animation);


                EditorCurveBinding spriteBinding = new EditorCurveBinding();
                spriteBinding.type = typeof(SpriteRenderer);
                spriteBinding.path = "";
                spriteBinding.propertyName = "m_Sprite";


                int length = animation.FrameTo - animation.FrameFrom + 1;
                ObjectReferenceKeyframe[] spriteKeyFrames = new ObjectReferenceKeyframe[length + 1]; // plus last frame to keep the duration

                float time = 0;

                int from = (animation.Animation != LoopAnimation.Reverse) ? animation.FrameFrom : animation.FrameTo;
                int step = (animation.Animation != LoopAnimation.Reverse) ? 1 : -1;

                int keyIndex = from;

                for (int i = 0; i < length; i++)
                {
                    if (i >= length)
                    {
                        keyIndex = from;
                    }

                    ObjectReferenceKeyframe frame = new ObjectReferenceKeyframe();
                    frame.time = time;
                    frame.value = sprites[keyIndex];

                    time += aseFile.Frames[keyIndex].FrameDuration / 1000f;

                    keyIndex += step;
                    spriteKeyFrames[i] = frame;
                }

                float frameTime = 1f / animationClip.frameRate;

                ObjectReferenceKeyframe lastFrame = new ObjectReferenceKeyframe();
                lastFrame.time = time - frameTime;
                lastFrame.value = sprites[keyIndex - step];

                spriteKeyFrames[spriteKeyFrames.Length - 1] = lastFrame;

                AnimationUtility.SetObjectReferenceCurve(animationClip, spriteBinding, spriteKeyFrames);
                AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(animationClip);

                switch (animation.Animation)
                {
                    case LoopAnimation.Forward:
                        animationClip.wrapMode = WrapMode.Loop;
                        settings.loopTime = true;
                        break;
                    case LoopAnimation.Reverse:
                        animationClip.wrapMode = WrapMode.Loop;
                        settings.loopTime = true;
                        break;
                    case LoopAnimation.PingPong:
                        animationClip.wrapMode = WrapMode.PingPong;
                        settings.loopTime = true;
                        break;
                }

                if (!importSettings.loopTime)
                {
                    animationClip.wrapMode = WrapMode.Once;
                    settings.loopTime = false;
                }

                AnimationUtility.SetAnimationClipSettings(animationClip, settings);
                ctx.AddObjectToAsset(animation.TagName, animationClip);

                index++;
            }

            animationSettings = animSettings.ToArray();
        }

        private void RemoveUnusedAnimationSettings(List<AseFileAnimationSettings> animationSettings,
            FrameTag[] animations)
        {
            for (int i = 0; i < animationSettings.Count; i++)
            {
                bool found = false;
                if (animationSettings[i] != null)
                {
                    foreach (var anim in animations)
                    {
                        if (animationSettings[i].animationName == anim.TagName)
                            found = true;
                    }
                }

                if (!found)
                {
                    animationSettings.RemoveAt(i);
                    i--;
                }
            }
        }

        public AseFileAnimationSettings GetAnimationSettingFor(List<AseFileAnimationSettings> animationSettings,
            FrameTag animation)
        {
            if (animationSettings == null)
                animationSettings = new List<AseFileAnimationSettings>();

            for (int i = 0; i < animationSettings.Count; i++)
            {
                if (animationSettings[i].animationName == animation.TagName)
                    return animationSettings[i];
            }

            animationSettings.Add(new AseFileAnimationSettings(animation.TagName));
            return animationSettings[animationSettings.Count - 1];
        }

        private string GetAnimationAbout(FrameTag animation)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendFormat("Animation Type:\t{0}", animation.Animation.ToString());
            sb.AppendLine();
            sb.AppendFormat("Animation:\tFrom: {0}; To: {1}", animation.FrameFrom, animation.FrameTo);

            return sb.ToString();
        }
    }
}