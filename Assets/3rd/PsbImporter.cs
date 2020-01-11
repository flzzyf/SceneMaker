using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.Experimental.AssetImporters;
using SubjectNerd.PsdImporter.PsdParser;

[ScriptedImporter(1, "psb")]
public class PsbImporter : ScriptedImporter
{
    #region 属性

    public float pixelsPerUnit = 100;
    public Vector2 pivot = new Vector2(.5f, 0);
    public FilterMode filterMode;

    //是否创建黑边
    public bool createBlackScreens = true;
    //黑边扩展倍率
    public float blackScreenExtrudeMultiplier = 2;
    //黑边
    public SortingLayer blackScreenLayer;

    #endregion

    #region 字段

    //根据Sprite的锚点计算初始位置
    Vector2 originPos { get { return new Vector2(pivot.x * psd.Width, pivot.y * psd.Height) / pixelsPerUnit; } }

    PsdDocument psd;

    GameObject main;

    //当前图层顺序
    int orderInLayer;

    AssetImportContext ctx;

    //用来判断是否有重名图层的字典
    Dictionary<string, int> nameExistDic;

    #endregion

    public override void OnImportAsset(AssetImportContext ctx)
    {
        nameExistDic = new Dictionary<string, int>();

        string name = GetFileName(ctx.assetPath);
        main = new GameObject(name);

        ctx.AddObjectToAsset(name, main);
        ctx.SetMainObject(main);

        this.ctx = ctx;

        LoadPSD(ctx.assetPath);
        CreateTextures();

        UnloadPSD();
    }

    //加载PSD
    void LoadPSD(string path)
    {
        //判断是ps文件
        if (path.EndsWith(".psb"))
        {
            psd = PsdDocument.Create(path);
        }
    }
    //卸载PSD文件
    void UnloadPSD()
    {
        psd.Dispose();

        psd = null;
    }

    //开始创建贴图
    void CreateTextures()
    {
        CreateLayers(psd.Childs, main.transform);

        if(createBlackScreens)
            CreateBlackScreens();
    }

    //输入PSD图层来创建图层
    void CreateLayers(IPsdLayer[] layers, Transform parent)
    {
        foreach (var layer in layers)
        {
            //如果是空图层，而且没有子物体，跳过
            if(layer.HasImage == false && layer.Childs.Length == 0)
            {
                continue;
            }

            string layerName = layer.Name;

            if(nameExistDic.ContainsKey(layer.Name))
            {
                layerName += "(" + (nameExistDic[layer.Name]) + ")";
            }

            GameObject go = new GameObject(layerName);
            go.transform.SetParent(parent);
            go.transform.SetAsLastSibling();

            //如果是空图层，不设置贴图
            if (layer.HasImage)
            {
                Sprite sprite = null;

                //寻找同名贴图
                //string[] findedSprites = UnityEditor.AssetDatabase.FindAssets(layer.Name + " t:sprite");
                //if (findedSprites.Length > 0)
                //{
                //    sprite = AssetDatabase.LoadAssetAtPath<Sprite>(AssetDatabase.GUIDToAssetPath(findedSprites[0]));
                //}

                //如果没有同名贴图
                if (sprite == null)
                {
                    Texture2D tex = GetTexture2D(layer);
                    tex.name = layerName;
                    sprite = Sprite.Create(tex, new Rect(0, 0, layer.Width, layer.Height), new Vector2(.5f, .5f), pixelsPerUnit, 0, SpriteMeshType.FullRect);
                    sprite.name = layerName;

                    ctx.AddObjectToAsset("tex_" + tex.name, tex);
                    ctx.AddObjectToAsset("sprite_" + sprite.name, sprite);
                }

                //设置贴图
                SpriteRenderer spriteRenderer = go.AddComponent<SpriteRenderer>();
                spriteRenderer.sprite = sprite;

                //设置图层顺序
                spriteRenderer.sortingOrder = orderInLayer;
                orderInLayer++;

                //计算位置
                int bottom = psd.Height - layer.Bottom;
                Vector2 pos = new Vector2(layer.Left + layer.Width / 2f, bottom + layer.Height / 2f) / pixelsPerUnit;
                pos -= originPos;

                go.transform.position = pos;

                //根据图层可见性设置物体可见性
                go.SetActive(layer is PsdLayer ? (layer as PsdLayer).IsVisible : true);

                ctx.AddObjectToAsset(go.name, go);

                //添加已存在名称字典
                if (nameExistDic.ContainsKey(layer.Name) == false)
                {
                    nameExistDic.Add(layer.Name, 1);
                }
                else
                {
                    nameExistDic[layer.Name]++;
                }
            }

            //如果有子物体，递归创建下去
            if (layer.Childs.Length > 0)
            {
                CreateLayers(layer.Childs, go.transform);
            }
        }
    }

    //创建四周黑边
    void CreateBlackScreens()
    {
        //偏移信息
        Vector2[] offsets = { new Vector2(1, .5f), new Vector2(.5f, 1), new Vector2(0, .5f), new Vector2(.5f, 0) };
        Vector2[] dirs = { new Vector2(1, 0), new Vector2(0, 1), new Vector2(-1, 0), new Vector2(0, -1) };

        for (int i = 0; i < 4; i++)
        {
            offsets[i] -= pivot;
        }

        //创建黑色贴图
        Texture2D tex = new Texture2D(1, 1);
        for (int y = 0; y < tex.height; y++)
        {
            for (int x = 0; x < tex.width; x++)
            {
                tex.SetPixel(x, y, Color.black);
            }
        }
        tex.name = "BlackScreen";
        ctx.AddObjectToAsset("tex_" + tex.name, tex);

        Sprite sprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(.5f, .5f), pixelsPerUnit);
        sprite.name = "BlackScreen";

        ctx.AddObjectToAsset("sprite_" + sprite.name, sprite);

        //创建黑边的文件夹
        GameObject parent = new GameObject("BlackScreens");
        parent.transform.SetParent(main.transform);
        ctx.AddObjectToAsset("blackScreenParent", parent);

        //创建四块黑边
        for (int i = 0; i < 4; i++)
        {
            GameObject blackScreen = new GameObject("BlackScreen" + i);
            blackScreen.transform.SetParent(parent.transform);

            //设置宽度
            float width = psd.Width * (dirs[i].y != 0 ? blackScreenExtrudeMultiplier : 1);
            float height = psd.Height * (dirs[i].x != 0 ? blackScreenExtrudeMultiplier : 1);
            blackScreen.transform.localScale = new Vector2(width, height);

            //设置贴图
            SpriteRenderer spriteRenderer = blackScreen.AddComponent<SpriteRenderer>();
            spriteRenderer.sprite = sprite;
            spriteRenderer.sortingOrder = 99;

            spriteRenderer.sortingLayerName = "BlackScreen";

            //设置位置
            Vector2 targetPos = new Vector2(psd.Width * offsets[i].x, psd.Height * offsets[i].y) / pixelsPerUnit;
            targetPos += new Vector2(dirs[i].x * psd.Width, dirs[i].y * psd.Height) / 2 / pixelsPerUnit;
            blackScreen.transform.position = targetPos;

            ctx.AddObjectToAsset(blackScreen.name + i, blackScreen);
        }
    }

    #region 帮助方法

    //从路径获取文件名
    private string GetFileName(string assetPath)
    {
        string[] parts = assetPath.Split('/');
        string filename = parts[parts.Length - 1];

        return filename.Substring(0, filename.LastIndexOf('.'));
    }

    //生成PS图层的贴图
    Texture2D GetTexture2D(IPsdLayer layer)
    {
        byte[] data = layer.MergeChannels();
        var channelCount = layer.Channels.Length;
        var pitch = layer.Width * layer.Channels.Length;
        var w = layer.Width;
        var h = layer.Height;

        var format = channelCount == 3 ? TextureFormat.RGB24 : TextureFormat.ARGB32;
        var tex = new Texture2D(w, h, format, false);
        var colors = new Color32[data.Length / channelCount];

        var k = 0;
        for (var y = h - 1; y >= 0; --y)
        {
            for (var x = 0; x < pitch; x += channelCount)
            {
                var n = x + y * pitch;
                var c = new Color32();
                if (channelCount == 5)
                {
                    c.b = data[n++];
                    c.g = data[n++];
                    c.r = data[n++];
                    n++;
                    c.a = (byte)Mathf.RoundToInt((float)(data[n++]) * layer.Opacity);
                }
                else if (channelCount == 4)
                {
                    c.b = data[n++];
                    c.g = data[n++];
                    c.r = data[n++];
                    c.a = (byte)Mathf.RoundToInt((float)data[n++] * layer.Opacity);
                }
                else
                {
                    c.b = data[n++];
                    c.g = data[n++];
                    c.r = data[n++];
                    c.a = (byte)Mathf.RoundToInt(layer.Opacity * 255f);
                }
                colors[k++] = c;
            }
        }
        tex.SetPixels32(colors);
        tex.Apply(false, true);

        tex.filterMode = filterMode;

        return tex;
    }

    #endregion
}
