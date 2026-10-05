// Human-approved temporary save/close/restore of SampleScene for Player validation.
public static class PlayerSceneIsolation
{
    const string Id="8455f557201b4988a18773741448050d";
    const string Folder="Assets/WhimTexTestMigration/"+Id;
    const string ScenePath=Folder+"/PlayerBootstrap.unity";
    const string UserPath="Assets/Scenes/SampleScene.unity";
    const string Journal="Temp/WhimTex/player-scene-isolation-"+Id+".json";
    [System.Serializable] public sealed class State
    { public string id,folderGuid,sceneGuid; public bool userWasActive,userWasDirty; public string phase; }
    static void Idle()
    {
        if(UnityEditor.BuildPipeline.isBuildingPlayer||UnityEditor.EditorApplication.isCompiling||UnityEditor.EditorApplication.isUpdating
            ||UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode)throw new System.InvalidOperationException("Editor not idle");
    }
    static void NoLinks(string path)
    {
        for(string p=System.IO.Path.GetFullPath(path);p!=null;p=System.IO.Path.GetDirectoryName(p))
            if((System.IO.File.Exists(p)||System.IO.Directory.Exists(p))&&(System.IO.File.GetAttributes(p)&System.IO.FileAttributes.ReparsePoint)!=0)
                throw new System.IO.IOException("Redirected isolation path: "+p);
    }
    static void NoTreeLinks()
    {
        NoLinks(Folder);var pending=new System.Collections.Generic.Stack<string>();pending.Push(Folder);
        while(pending.Count>0){string p=pending.Pop();NoLinks(p);if(System.IO.Directory.Exists(p))
            foreach(string entry in System.IO.Directory.EnumerateFileSystemEntries(p))pending.Push(entry);}
    }
    static void Write(State state)
    {
        NoLinks(Journal);string bytes=UnityEngine.JsonUtility.ToJson(state);
        if(System.IO.File.Exists(Journal))System.IO.File.WriteAllText(Journal,bytes);
        else using(var stream=new System.IO.FileStream(Journal,System.IO.FileMode.CreateNew))
        using(var writer=new System.IO.StreamWriter(stream))writer.Write(bytes);
    }
    public static string Prepare()
    {
        Idle();NoLinks(Folder);NoLinks(ScenePath);NoLinks(UserPath);NoLinks(Journal);
        if(System.IO.File.Exists(Journal)||System.IO.Directory.Exists(Folder)||System.IO.File.Exists(Folder+".meta"))
            throw new System.InvalidOperationException("Fresh owned isolation identity required");
        var user=UnityEngine.SceneManagement.SceneManager.GetSceneByPath(UserPath);
        if(!user.IsValid()||!user.isLoaded||!System.IO.File.Exists(UserPath))throw new System.InvalidOperationException("Authorized scene is not loaded");
        for(int i=0;i<UnityEngine.SceneManagement.SceneManager.sceneCount;i++)
        {var s=UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);if(s.path!=UserPath&&s.isDirty)throw new System.InvalidOperationException("Another user scene is dirty; no authority to close it");}
        var state=new State{id=Id,userWasActive=UnityEngine.SceneManagement.SceneManager.GetActiveScene()==user,userWasDirty=user.isDirty,phase="preparing"};
        if(!UnityEditor.SceneManagement.EditorSceneManager.SaveScene(user)||user.isDirty)throw new System.InvalidOperationException("Authorized scene save failed");
        if(!UnityEditor.AssetDatabase.IsValidFolder("Assets/WhimTexTestMigration"))UnityEditor.AssetDatabase.CreateFolder("Assets","WhimTexTestMigration");
        state.folderGuid=UnityEditor.AssetDatabase.CreateFolder("Assets/WhimTexTestMigration",Id);
        if(string.IsNullOrEmpty(state.folderGuid)||!UnityEditor.AssetDatabase.IsValidFolder(Folder))throw new System.InvalidOperationException("Owned folder creation failed");
        Write(state);
        var test=UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,UnityEditor.SceneManagement.NewSceneMode.Additive);
        if(!UnityEditor.SceneManagement.EditorSceneManager.SaveScene(test,ScenePath))throw new System.InvalidOperationException("Owned empty scene save failed");
        state.sceneGuid=UnityEditor.AssetDatabase.AssetPathToGUID(ScenePath); Write(state);
        if(!UnityEngine.SceneManagement.SceneManager.SetActiveScene(test)||!UnityEditor.SceneManagement.EditorSceneManager.CloseScene(user,true))
            throw new System.InvalidOperationException("Authorized temporary scene close failed; retain journal");
        state.phase="isolated"; Write(state);
        return UnityEngine.JsonUtility.ToJson(state);
    }
    public static string Restore()
    {
        Idle();NoLinks(Journal);NoLinks(UserPath);NoTreeLinks();var state=UnityEngine.JsonUtility.FromJson<State>(System.IO.File.ReadAllText(Journal));
        if(state.id!=Id||state.phase!="isolated"||UnityEditor.AssetDatabase.AssetPathToGUID(Folder)!=state.folderGuid
            ||UnityEditor.AssetDatabase.AssetPathToGUID(ScenePath)!=state.sceneGuid)throw new System.InvalidOperationException("Owned journal/assets differ");
        var user=UnityEngine.SceneManagement.SceneManager.GetSceneByPath(UserPath);
        if(!user.IsValid()||!user.isLoaded)user=UnityEditor.SceneManagement.EditorSceneManager.OpenScene(UserPath,UnityEditor.SceneManagement.OpenSceneMode.Additive);
        if(!user.IsValid()||!user.isLoaded)throw new System.InvalidOperationException("User scene restore failed; preserve fixture");
        if(state.userWasActive&&!UnityEngine.SceneManagement.SceneManager.SetActiveScene(user))throw new System.InvalidOperationException("Active scene restore failed");
        var test=UnityEngine.SceneManagement.SceneManager.GetSceneByPath(ScenePath);
        if(test.IsValid()&&test.isLoaded&&!UnityEditor.SceneManagement.EditorSceneManager.CloseScene(test,true))throw new System.InvalidOperationException("Owned scene close failed");
        if(!UnityEditor.AssetDatabase.DeleteAsset(Folder)||System.IO.Directory.Exists(Folder)||System.IO.File.Exists(Folder+".meta"))throw new System.InvalidOperationException("Exact owned isolation folder remains");
        state.phase="restored"; Write(state);return UnityEngine.JsonUtility.ToJson(state);
    }
    // Continue the known partial preparation without recreating any fixture or replaying save.
    public static string ContinuePrepare()
    {
        Idle();NoLinks(Journal);NoLinks(UserPath);NoTreeLinks();
        var state=UnityEngine.JsonUtility.FromJson<State>(System.IO.File.ReadAllText(Journal));
        if(state.id!=Id||state.phase!="preparing"||UnityEditor.AssetDatabase.AssetPathToGUID(Folder)!=state.folderGuid
            ||UnityEditor.AssetDatabase.AssetPathToGUID(ScenePath)!=state.sceneGuid)
            throw new System.InvalidOperationException("Wrong partial isolation identity");
        var user=UnityEngine.SceneManagement.SceneManager.GetSceneByPath(UserPath);
        var test=UnityEngine.SceneManagement.SceneManager.GetSceneByPath(ScenePath);
        if(!user.IsValid()||!user.isLoaded||user.isDirty||!test.IsValid()||!test.isLoaded||test.isDirty)
            throw new System.InvalidOperationException("Both authorized scenes must be loaded and already saved");
        for(int i=0;i<UnityEngine.SceneManagement.SceneManager.sceneCount;i++)
        {var s=UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);if(s.path!=UserPath&&s.path!=ScenePath)
            throw new System.InvalidOperationException("No authority to replace other loaded scenes");}
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene(ScenePath,UnityEditor.SceneManagement.OpenSceneMode.Single);
        user=UnityEngine.SceneManagement.SceneManager.GetSceneByPath(UserPath);
        test=UnityEngine.SceneManagement.SceneManager.GetSceneByPath(ScenePath);
        if(user.IsValid()&&user.isLoaded||!test.IsValid()||!test.isLoaded||UnityEngine.SceneManagement.SceneManager.sceneCount!=1
            ||UnityEngine.SceneManagement.SceneManager.GetActiveScene().path!=ScenePath)
            throw new System.InvalidOperationException("Partial isolation continuation did not complete");
        state.phase="isolated";Write(state);return UnityEngine.JsonUtility.ToJson(state);
    }
}
