using UnityEngine;
using UnityEngine.SceneManagement;

public static class TransitionLoader
{
    public static string NextSceneName;
    public static bool FromRight = true;
    public static int RequestedFps = 24;

    /// <summary>
    /// Load a scene via the TransitionScene that plays a page-curl.
    /// </summary>
    public static void Go(string nextSceneName, bool fromRight = true, int fps = 24)
    {
        NextSceneName = nextSceneName;
        FromRight = fromRight;
        RequestedFps = fps;

        // Jump to the transition scene. It will read the static fields above.
        SceneManager.LoadScene("TransitionScene", LoadSceneMode.Single);
    }
}

