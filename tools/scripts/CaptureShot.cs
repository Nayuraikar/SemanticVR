using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
public static class CaptureShot
{
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/MainScene.unity");
        var cam = Camera.main ?? Object.FindFirstObjectByType<Camera>();
        Debug.Log("CAM " + (cam ? cam.name + " " + cam.transform.position + " " + cam.transform.eulerAngles : "none"));
        var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
        cam.targetTexture = rt; cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); tex.Apply();
        System.IO.File.WriteAllBytes(System.Environment.GetEnvironmentVariable("SHOT_OUT"), tex.EncodeToPNG());
    }
}
