public static class PlayerSceneIsolationInspection
{
    public static string Run()
    {
        var result=new System.Text.StringBuilder("{\"activePath\":\"");
        result.Append(UnityEngine.SceneManagement.SceneManager.GetActiveScene().path.Replace("\\","\\\\").Replace("\"","\\\""));
        result.Append("\",\"scenes\":[");
        for(int i=0;i<UnityEngine.SceneManagement.SceneManager.sceneCount;i++)
        {
            var scene=UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
            if(i>0)result.Append(',');
            result.Append("{\"path\":\"").Append(scene.path.Replace("\\","\\\\").Replace("\"","\\\""));
            result.Append("\",\"valid\":").Append(scene.IsValid()?"true":"false");
            result.Append(",\"loaded\":").Append(scene.isLoaded?"true":"false");
            result.Append(",\"dirty\":").Append(scene.isDirty?"true":"false");
            result.Append('}');
        }
        return result.Append("]}").ToString();
    }
}
