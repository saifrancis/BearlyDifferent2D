using UnityEngine;
using UnityEngine.SceneManagement;

public static class TransitionLoader
{
    public static string NextSceneName;
    public static bool FromRight = true;
    public static int RequestedFps = 24;

    public static void Go(string nextSceneName, bool fromRight = true, int fps = 24)
    {
        NextSceneName = nextSceneName;
        FromRight = fromRight;
        RequestedFps = fps;

        SceneManager.LoadScene("TransitionScene", LoadSceneMode.Single);
    }
}

