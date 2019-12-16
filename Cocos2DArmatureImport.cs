using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Xml;
using System.Xml.Serialization;
using UnityEngine;
using UnityEditor;


// COCOS2D ARMATURE IMPORT V1

public class Cocos2DArmatureImport
{

    public const float scale_factor = 100.0f;
    public const float sample_rate = 60.0f;

    // Imports animation from Cocos2D given an exported armature data file and its associated texture atlas and config data
    [MenuItem("Character/Import from Cocos2D")]
    public static void ProcessCharacter()
    {

        // Get the file paths of the data
        string plist_path = EditorUtility.OpenFilePanel("Open character's config data:", "Assets", "plist");
        string json_path = EditorUtility.OpenFilePanel("Open character's skeletal/animation data:", "Assets", "ExportJson");

        Texture2D sprite;

        if (Selection.objects.Length != 1)
            sprite = null;
        else
            sprite = Selection.activeObject as Texture2D;

        if (sprite == null)
        {
	    Debug.LogError("ERROR: Sprite sheet is not selected.");
            return;
        }

        if (plist_path == "" || json_path == "")
        {
	    Debug.LogError("ERROR: Plist and/or JSON are null.");
            return;
        }


        // Read sprite data from the plist
        List<SpriteData> cutting_data = ReadPlist(plist_path);

        // Cut sprite data
        List<SpriteMetaData> sheet = CutSprite(sprite, cutting_data);

        // Prepare sprite atlas
        string atlas_source = AssetDatabase.GetAssetPath(sprite);
        atlas_source = atlas_source.Substring(atlas_source.LastIndexOf("/") + 1, (atlas_source.LastIndexOf(".") - atlas_source.LastIndexOf("/")) - 1);
        SpriteAtlas atlas = new SpriteAtlas(atlas_source);


        // Read data from Json file into Json string
        string json_content = File.ReadAllText(json_path);

        // Deserialize Json string into usable object data
        RootObject obj = JsonUtility.FromJson<RootObject>(json_content);


        // Create base object for armature
        GameObject root = new GameObject(obj.armature_data[0].name);
        root.tag = "Armature";

        // Prepare the skeleton
        List<SpriteBone> bones = new List<SpriteBone>();


        // For each bone in the skeleton
        foreach (BoneData node in obj.armature_data[0].bone_data)
        {
            // Create a new game object, tag is set to bone
            GameObject g = new GameObject(node.name);
            g.tag = "Bone";

            // If parent node is null, parent the object to the root
            if (node.parent == "")
                g.transform.SetParent(root.transform);
            else
            {
                // Get a reference to all child transforms
                Transform[] allChildren = root.GetComponentsInChildren<Transform>();

                // For each transform
                foreach (Transform child in allChildren)
                {
                    // Check if transform name matches parent. If so, parent the object to that transform
                    if (child.gameObject.name == node.parent)
                    {
                        g.transform.SetParent(child);
                    }
                }
            }

            // POSITION IS RELATIVE TO PARENT
            // Set the position and scale
            // Mathf.Rad2Deg
            g.transform.localPosition = new Vector2((float)node.x / scale_factor, (float)node.y / scale_factor);
            g.transform.localScale = new Vector3((float)node.cX, (float)node.cY, 1.0f);
            g.transform.localEulerAngles = new Vector3(0.0f, 0.0f, (float)(node.kY * Mathf.Rad2Deg));

            int index = 0;
            int displayIndex = node.dI;

            /*if (displayIndex < 0)
                displayIndex = 0;*/

            // Initialize skeleton
            SpriteBone sb = new SpriteBone();
            sb.bone = g;


            // Sprites need to be their own gameobjects
            foreach (DisplayData d in node.display_data)
            {
                // Create object
                GameObject s = new GameObject(d.name);
                s.transform.SetParent(g.transform);
                s.tag = "Display Data";

                SkinData d_data = d.skin_data[0];
                SpriteData sd = FindSpriteData(cutting_data, d.name);

                // Set sprite transforms
                if (sd != null)
                {
                    s.transform.localPosition = new Vector3(((float)d_data.x) / scale_factor, ((float)d_data.y) / scale_factor);
                    s.transform.localScale = new Vector3((float)d_data.cX, (float)d_data.cY, 1.0f);
                    s.transform.localEulerAngles = new Vector3(0.0f, 0.0f, (float)(d_data.kY * Mathf.Rad2Deg));

                    // Set sprite offset
                    GameObject o = new GameObject(index.ToString());
                    o.transform.SetParent(s.transform);
                    o.tag = "Sprite";

                    // Set up sprite components
                    o.AddComponent<SpriteRenderer>();
                    //o.GetComponent<SpriteRenderer>().sortingOrder = node.z;
                    Sprite body_piece = atlas.getSprite(d.name);
                    o.GetComponent<SpriteRenderer>().sprite = body_piece;

                    // Apply sprite offset
                    o.transform.localPosition = new Vector3(sd.offsetX / scale_factor, sd.offsetY / scale_factor, -node.z / scale_factor);
                    o.transform.localEulerAngles = Vector3.zero;

                    sb.displayData.Add(o);

                    // Determine visibility
                    if (displayIndex != index)
                        o.SetActive(false);
                }

                index++;
            }

            // Add bone to the list
            bones.Add(sb);
        }


        // Add an animator to the root
        root.AddComponent<Animator>();

        // Create an animator controller (ADDENDUM: MAY NOT BE NEEDED)

        // Loop through all animations in the JSON
        foreach (MovData md in obj.animation_data[0].mov_data)
        {
            // Create and name an animation clip
            AnimationClip anim = new AnimationClip();
            anim.name = md.name;

            // Name and frame index of inverted bones
            // Change to dictionary <string, Dictionary<int, bool>>
            Dictionary<string, Dictionary<int, bool>> toInvert = new Dictionary<string, Dictionary<int, bool>>();

            foreach(MovBoneData mbd in md.mov_bone_data)
            {
                Dictionary<int, bool> boneInv = new Dictionary<int, bool>();
                int numAdded = 0;

                foreach(FrameData fd in mbd.frame_data)
                {
                    if (fd.cX < 0 || fd.cY < 0)
                    {
                        boneInv.Add(fd.fi, true);
                        numAdded++;
                    }
                    else
                    {
                        if(!boneInv.ContainsKey(fd.fi))
                            boneInv.Add(fd.fi, false);
                    }
                }

                if (boneInv.Count > 0 && numAdded > 0)
                    toInvert.Add(mbd.name, boneInv);
            }


            // Loop through all bone data
            foreach (MovBoneData mbd in md.mov_bone_data)
            {
                // Name of bone that needs its transform inverted
                //List<string> toInvert = new List<string>();

                // Find the game object matching the bone
                SpriteBone bone = null;

                foreach (SpriteBone sb in bones)
                {
                    if (sb.bone.name.Equals(mbd.name))
                        bone = sb;
                }

                // Prepare animation curves for animated properties
                AnimationCurve curve_x = new AnimationCurve();
                AnimationCurve curve_y = new AnimationCurve();
                AnimationCurve curve_cx = new AnimationCurve();
                AnimationCurve curve_cy = new AnimationCurve();
                AnimationCurve curve_ky = new AnimationCurve();

                AnimationCurve[] curve_z = new AnimationCurve[bone.displayData.Count];
                AnimationCurve[] curve_di = new AnimationCurve[bone.displayData.Count];

                AnimationCurve[] curve_fx = new AnimationCurve[bone.displayData.Count];
                AnimationCurve[] curve_fy = new AnimationCurve[bone.displayData.Count];

                // Required to retain offsets
                AnimationCurve[] curve_zx = new AnimationCurve[bone.displayData.Count];
                AnimationCurve[] curve_zy = new AnimationCurve[bone.displayData.Count];

                for(int i=0; i < bone.displayData.Count; i++)
                {
                    curve_z[i] = new AnimationCurve();
                    curve_zx[i] = new AnimationCurve();
                    curve_zy[i] = new AnimationCurve();
                    curve_di[i] = new AnimationCurve();
                    curve_fx[i] = new AnimationCurve();
                    curve_fy[i] = new AnimationCurve();
                }

                int previous_z = 0;

                // Loop through all keyframes for that bone
                foreach (FrameData fd in mbd.frame_data)
                {
                    // Set keyframe values for position, rotation, and scale. Note that all of these are relative
                    Keyframe px = new Keyframe((fd.fi / sample_rate), (bone.bone.transform.localPosition.x + ((float)fd.x / scale_factor)));
                    px.tangentMode = 0;
                    px.inTangent = 0.0f;
                    px.outTangent = 0.0f;

                    Keyframe py = new Keyframe((fd.fi / sample_rate), (bone.bone.transform.localPosition.y + ((float)fd.y / scale_factor)));
                    py.tangentMode = 0;
                    py.inTangent = 0.0f;
                    py.outTangent = 0.0f;

                    curve_x.AddKey(px);
                    curve_y.AddKey(py);

                    // Scale keyframes
                    Keyframe kx = new Keyframe((fd.fi / sample_rate), ((float)fd.cX));
                    kx.tangentMode = 0;
                    kx.inTangent = 0.0f;
                    kx.outTangent = 0.0f;

                    Keyframe ky = new Keyframe((fd.fi / sample_rate), ((float)fd.cY));
                    ky.tangentMode = 0;
                    ky.inTangent = 0.0f;
                    ky.outTangent = 0.0f;

                    curve_cx.AddKey(kx);
                    curve_cy.AddKey(ky);

                    // This is the ONLY bug remaining. When something's supposed to be inverted, the inversion is never reset.
                    // Inversion is also not 100%. For example, Suzy's right arm is not inverted in her idle animation


                    // Correct inversion error
                    Transform t = bone.bone.transform;
                    float direction = 1.0f;

                    while (t.transform.parent != null)
                    {
                        t = t.transform.parent;

                        if (toInvert.ContainsKey(t.gameObject.name))
                        {
                            // This needs to get the previous frame index if none exist
                            if (InvertAtIndex(toInvert, fd.fi, t.gameObject.name))
                            {
                                direction = -1.0f;
                                Debug.Log("Success! - " + md.name + ": " + t.gameObject.name + " " + mbd.name);
                            }
                        }
                    }
                    // Invert rotation
                    Keyframe ry = new Keyframe((fd.fi / sample_rate), 
                        direction * (bone.bone.transform.localEulerAngles.z + ((float)(fd.kY * Mathf.Rad2Deg))));
                    ry.tangentMode = 0;
                    ry.inTangent = 0.0f;
                    ry.outTangent = 0.0f;

                    curve_ky.AddKey(ry);

                    // Determine display index
                    int displayIndex = fd.dI;

                    for (int i = 0; i < bone.displayData.Count; i++)
                    {
                        // If the frame index is not one, a second keyframe will need to be set to disable "sliding"
                        if (fd.fi > 1)
                        {
                            Keyframe p_zx = new Keyframe(((fd.fi - 1) / sample_rate), bone.displayData[i].transform.localPosition.x);
                            p_zx.tangentMode = 0;
                            p_zx.inTangent = 0.0f;
                            p_zx.outTangent = 0.0f;

                            Keyframe p_zy = new Keyframe(((fd.fi - 1) / sample_rate), bone.displayData[i].transform.localPosition.y);
                            p_zy.tangentMode = 0;
                            p_zy.inTangent = 0.0f;
                            p_zy.outTangent = 0.0f;

                            Keyframe p_zz = new Keyframe(((fd.fi - 1) / sample_rate), bone.displayData[i].transform.localPosition.z + (-previous_z / scale_factor));
                            p_zz.tangentMode = 0;
                            p_zz.inTangent = 0.0f;
                            p_zz.outTangent = 0.0f;

                            curve_z[i].AddKey(p_zz);
                            curve_zx[i].AddKey(p_zx);
                            curve_zy[i].AddKey(p_zy);

                        }

                        Keyframe zx = new Keyframe((fd.fi / sample_rate), bone.displayData[i].transform.localPosition.x);
                        zx.tangentMode = 0;
                        zx.inTangent = 0.0f;
                        zx.outTangent = 0.0f;

                        Keyframe zy = new Keyframe((fd.fi / sample_rate), bone.displayData[i].transform.localPosition.y);
                        zy.tangentMode = 0;
                        zy.inTangent = 0.0f;
                        zy.outTangent = 0.0f;

                        Keyframe zz = new Keyframe((fd.fi / sample_rate), bone.displayData[i].transform.localPosition.z + (-fd.z / scale_factor));
                        zz.tangentMode = 0;
                        zz.inTangent = 0.0f;
                        zz.outTangent = 0.0f;

                        curve_z[i].AddKey(zz);
                        previous_z = fd.z;

                        curve_zx[i].AddKey(zx);
                        curve_zy[i].AddKey(zy);

                        // Check if the current index equals the display index
                        
                        if (bone.displayData[i].name.Equals(displayIndex.ToString()))
                        {
                            Keyframe k = new Keyframe((fd.fi / sample_rate), 1.0f);
                            k.tangentMode = 103;
                            k.inTangent = float.PositiveInfinity;
                            k.outTangent = float.PositiveInfinity;
                            curve_di[i].AddKey(k);
                        }

                        else
                        {
                            Keyframe k = new Keyframe((fd.fi / sample_rate), 0.0f);
                            k.tangentMode = 103;
                            k.inTangent = float.PositiveInfinity;
                            k.outTangent = float.PositiveInfinity;
                            curve_di[i].AddKey(k);
                        }
                    }

                }

                // Get full name of bone
                string bone_name = GetCompletePath(bone.bone.transform);

                // Apply transform keyframes to animation clip
                anim.SetCurve(bone_name, typeof(Transform), "localPosition.x", curve_x);
                anim.SetCurve(bone_name, typeof(Transform), "localPosition.y", curve_y);
                anim.SetCurve(bone_name, typeof(Transform), "localScale.x", curve_cx);
                anim.SetCurve(bone_name, typeof(Transform), "localScale.y", curve_cy);
                anim.SetCurve(bone_name, typeof(Transform), "localEulerAngles.z", curve_ky);

                // Get full name of sprites
                string[] sprite_names = new string[bone.displayData.Count];

                for (int i = 0; i < sprite_names.Length; i++)
                {
                    sprite_names[i] = GetCompletePath(bone.displayData[i].transform);

                    // Apply sprite keyframes to animation clip
                    anim.SetCurve(sprite_names[i], typeof(Transform), "localPosition.x", curve_zx[i]);
                    anim.SetCurve(sprite_names[i], typeof(Transform), "localPosition.y", curve_zy[i]);
                    anim.SetCurve(sprite_names[i], typeof(Transform), "localPosition.z", curve_z[i]);

                    anim.SetCurve(sprite_names[i], typeof(GameObject), "m_IsActive", curve_di[i]);
                }
            }

            // Apply loop
            AnimationClipSettings animClipSett = new AnimationClipSettings();
            animClipSett.loopTime = md.lp;

            AnimationUtility.SetAnimationClipSettings(anim, animClipSett);

            // Add the animation to the animator

            // Create the anim clip
            AssetDatabase.CreateAsset(anim, "assets/_animation/" + anim.name + ".anim");
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

        }

    }

    // Reads data from the provided plist
    public static List<SpriteData> ReadPlist(string plist_path)
    {
        // Plist data
        Plist data = null;

        // Serializer for the data
        XmlSerializer serializer = new XmlSerializer(typeof(Plist));

        // Read plist file into data
        StreamReader reader = new StreamReader(plist_path);
        data = (Plist)serializer.Deserialize(reader);
        reader.Close();

        // Prepare sprite data output
        List<Dict> sprite_data = data.Dict.dict;
        List<SpriteData> cutting_data = new List<SpriteData>();

        // Go through all dict in the plist
        foreach (Dict d in sprite_data)
        {
            // Current dictionary index
            int dict_index = 0;

            // Check if the key corresponds to a sprite
            foreach (string key in d.Key)
            {
                if (key.Substring(key.Length - 4).Equals(".png"))
                {
                    // Create new sprite data
                    SpriteData sd = new SpriteData();

                    // Set the sprite name
                    sd.name = key;

                    // Go one "layer" down to grab sprite data
                    Dict s_data = d.dict[dict_index];

                    int int_index = 0;
                    int float_index = 0;

                    // Check all the keys stored
                    foreach (string k in s_data.Key)
                    {
                        // Grab width value
                        if (k.Equals("width"))
                        {
                            sd.width = s_data.Integer[int_index];
                            int_index++;
                        }

                        // Grab height value
                        else if (k.Equals("height"))
                        {
                            sd.height = s_data.Integer[int_index];
                            int_index++;
                        }

                        // Grab x value
                        else if (k.Equals("originalWidth"))
                        {
                            sd.originalWidth = s_data.Integer[int_index];
                            int_index++;
                        }

                        // Grab y value
                        else if (k.Equals("originalHeight"))
                        {
                            sd.originalHeight = s_data.Integer[int_index];
                            int_index++;
                        }

                        // Grab x value
                        else if (k.Equals("x"))
                        {
                            sd.x = s_data.Integer[int_index];
                            int_index++;
                        }

                        // Grab y value
                        else if (k.Equals("y"))
                        {
                            sd.y = s_data.Integer[int_index];
                            int_index++;
                        }

                        // Grab offset x value
                        else if (k.Equals("offsetX"))
                        {
                            sd.offsetX = s_data.Real[float_index];
                            float_index++;
                        }

                        // Grab offset y value
                        else if (k.Equals("offsetY"))
                        {
                            sd.offsetY = s_data.Real[float_index];
                            float_index++;
                        }
                    }

                    // Add the sprite data to the list
                    cutting_data.Add(sd);

                    dict_index++;
                }
            }
        }

        // Return the data for the cut sprites
        return cutting_data;

    }


    // Cuts the sprite into its sub-sprites
    public static List<SpriteMetaData> CutSprite(Texture2D sprite, List<SpriteData> cutting_data)
    {
        // Import image
        string image_path = AssetDatabase.GetAssetPath(sprite);
        TextureImporter importer = AssetImporter.GetAtPath(image_path) as TextureImporter;
        importer.spriteImportMode = SpriteImportMode.Multiple;

        // Prepare spritesheet
        List<SpriteMetaData> sheet = new List<SpriteMetaData>();
        int source_y = sprite.height;

        // For each sub-sprite
        for (int i = 0; i < cutting_data.Count; i++)
        {
            // Create new sprite metadata
            SpriteMetaData s = new SpriteMetaData();
            SpriteData sd = cutting_data[i];

            s.name = sd.name;
            s.pivot = new Vector2(sd.offsetX, sd.offsetY);
            s.rect = new Rect(sd.x, source_y - sd.y - sd.height, sd.width, sd.height);

            s.alignment = 0;
            s.border = new Vector4(0.0f, 0.0f, 0.0f, 0.0f);

            sheet.Add(s);
        }


        // Apply spritesheet
        importer.spritesheet = sheet.ToArray();

        // Apply import settings
        AssetDatabase.StartAssetEditing();
        AssetDatabase.ImportAsset(importer.assetPath);
        AssetDatabase.StopAssetEditing();

        // Return spritesheet
        return sheet;
    }


    // Searches for and returns a sprite data corresponding to the given name
    public static SpriteData FindSpriteData(List<SpriteData> data, string name)
    {
        SpriteData sd = null;

        foreach (SpriteData s in data)
        {
            if (s.name.Equals(name))
                sd = s;
        }

        return sd;
    }


    // Returns the complete path of a game object
    public static string GetCompletePath(Transform t)
    {
        string name = t.gameObject.name;

        while (t.transform.parent != null)
        {
            t = t.transform.parent;

            if (t.transform.parent != null)
                name = t.gameObject.name + "/" + name;
        }

        return name;
    }


    // Returns true if the given frame index either exists, or exists after a keyframe. This is for inversion.
    public static bool InvertAtIndex(Dictionary<string, Dictionary<int, bool>> frames, int fi, string bone)
    {
        bool result = false;

        for(int i=0; i<=fi; i++)
        {
            if (frames[bone].ContainsKey(i))
                result = frames[bone][i];
        }

        return result;
    }
}

// NOTE: In the context of these classes, skew rotation is in radians.

// Transform data for sprite
[System.Serializable]
public class SkinData
{
    public double x; // X position
    public double y; // Y position
    public double cX; // X scale
    public double cY; // Y scale
    public double kX; // X skew
    public double kY; // Y skew
}


// Sprite
[System.Serializable]
public class DisplayData
{
    public string name; // Name of sprite
    public int displayType; // Whether or not sprite is visible(?)
    public List<SkinData> skin_data; // Transform data for sprite
}


// Bone
[System.Serializable]
public class BoneData
{
    public string name; // Name of bone
    public string parent; // Name of parent bone
    public int dI; // Display index
    public double x; // X position
    public double y; // Y position
    public int z; // Z index for layering
    public double cX; // X scale
    public double cY; // Y scale
    public double kX; // X skew
    public double kY; // Y skew
    public double arrow_x; // Unknown
    public double arrow_y; // Unknown
    public bool effectbyskeleton; // Whether or not bone is effected by skeleton
    public List<DisplayData> display_data; // Sprites controlled by bone
}


// Skeleton
[System.Serializable]
public class ArmatureData
{
    public string strVersion; // Completely useless
    public double version; // Completely useless
    public string name; // Name of skeleton
    public List<BoneData> bone_data; // Bones in skeleton
}


// Color
[System.Serializable]
public class Color
{
    public int a; // Alpha
    public int r; // Red
    public int g; // Green
    public int b; // Blue
}


// Keyframe of animation
[System.Serializable]
public class FrameData
{
    public int dI; // Display index
    public double x; // X position
    public double y; // Y position
    public int z; // Z index for layering
    public double cX; // X scale
    public double cY; // Y scale
    public double kX; // X rotation
    public double kY; // Y rotation
    public int fi; // Frame index
    public int twE; // Tween easing
    public bool tweenFrame; // Whether or not frame should be tweened from previous
    public int bd_src; // Blend src (source?)
    public int bd_dst; // Blend dst (distance?)
    public Color color; // Color
    public string evt; // Event
}


// Animation for individual bones
[System.Serializable]
public class MovBoneData
{
    public string name; // Name of bone
    public double dl; // Movement delay
    public List<FrameData> frame_data; // Keyframes
}


// Animation
[System.Serializable]
public class MovData
{
    public string name; // Name of animation
    public int dr; // Duration (frames)
    public bool lp; // Whether or not animation loops
    public int to; // Duration to (frames)
    public int drTW; // Duration tween (frames)
    public int twE; // Tween easing (usually zero)
    public float sc; // Movement scale
    public List<MovBoneData> mov_bone_data; // Bone data
}


// Contains all animations
[System.Serializable]
public class AnimationData
{
    public string name; // Name of sprite sheet
    public List<MovData> mov_data; // Animations
}


// References to each sprite
[System.Serializable]
public class TextureData
{
    public string name; // Sprite name
    public double width; // Width
    public double height; // Height
    public double pX; // X pivot
    public double pY; // Y pivot
    public string plistFile; // Always empty
}


// Root
[System.Serializable]
public class RootObject
{
    public double content_scale; // Scale
    public List<ArmatureData> armature_data; // Skeleton
    public List<AnimationData> animation_data; // Animations
    public List<TextureData> texture_data; // Textures
    public List<string> config_file_path; // Plist
    public List<string> config_png_path; // Texture Atlas
}


// Plist dictionary
[System.Serializable]
[XmlRoot(ElementName = "dict")]
public class Dict
{
    [XmlElement(ElementName = "key")]
    public List<string> Key { get; set; } // Key that indicates what the data that follows is
    [XmlElement(ElementName = "integer")]
    public List<int> Integer { get; set; } // Integer values
    [XmlElement(ElementName = "real")]
    public List<float> Real { get; set; } // Float valyes
    [XmlElement(ElementName = "string")]
    public List<string> String { get; set; } // String values
    [XmlElement(ElementName = "dict")]
    public List<Dict> dict { get; set; } // Sub-dictionaries
}


// Root of a plist
[System.Serializable]
[XmlRoot(ElementName = "plist")]
public class Plist
{
    [XmlElement(ElementName = "dict")]
    public Dict Dict { get; set; } // Data in plist
    [XmlAttribute(AttributeName = "version")]
    public string Version { get; set; } // Version of plist
}


// Holds cut sprite data values for ease of access
public class SpriteData
{
    public string name;
    public int width;
    public int height;
    public int originalWidth; // Probably useless, is here for completeness
    public int originalHeight; // Probably useless, is here for completeness
    public int x; // point to start cutting from
    public int y; // point to start cutting from
    public float offsetX;
    public float offsetY;

}


// Used to get individual sprites from an atlas
public class SpriteAtlas
{
    // Dictonary of sprites keyed by name
    public Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();


    // Constructor
    public SpriteAtlas(string path)
    {
        loadSprite(path);
    }


    // Loads the sprites at path
    public void loadSprite(string path)
    {
        Sprite[] allSprites = Resources.LoadAll<Sprite>(path);
        if (allSprites == null || allSprites.Length <= 0)
        {
            Debug.LogError("The Provided atlas `" + path + "` does not exist!");
            return;
        }

        for (int i = 0; i < allSprites.Length; i++)
        {
            sprites.Add(allSprites[i].name, allSprites[i]);
        }
    }


    //Get the provided sprite from the loaded sprites
    public Sprite getSprite(string name)
    {
        Sprite tempSprite;

        if (!sprites.TryGetValue(name, out tempSprite))
        {
            Debug.LogError("The Provided sprite `" + name + "` does not exist!");
            return null;
        }
        return tempSprite;
    }
}


// Used to hold all bone data
public class SpriteBone
{
    // Display Data for a bone
    public List<GameObject> displayData = new List<GameObject>();

    // Game object for a bone
    public GameObject bone;
}