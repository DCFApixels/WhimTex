// run_script entry TiffLiveSmoke.Run. Creates and removes only its own temporary assets.
using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using DCFApixels.WhimTex;

public static class TiffLiveSmoke
{
    private static void Check(bool value, string message) { if (!value) throw new Exception("FAIL: " + message); }

    private static string Revision(string response)
    {
        const string marker = "\"revision\":\"";
        int start = response.IndexOf(marker, StringComparison.Ordinal);
        Check(start >= 0, "inspect includes revision");
        start += marker.Length;
        int end = response.IndexOf('"', start);
        Check(end > start, "revision is readable");
        return response.Substring(start, end - start);
    }

    public static string Run()
    {
        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        string folder = "Assets/WhimTexTiffLive_" + Guid.NewGuid().ToString("N");
        string preview = "Temp/WhimTex/" + Guid.NewGuid().ToString("N") + ".png";
        string previewFull = Path.Combine(projectRoot, preview);
        string sessionId = "live-" + Guid.NewGuid().ToString("N");
        string conflictSessionId = "live-conflict-" + Guid.NewGuid().ToString("N");
        string createSessionId = "live-create-" + Guid.NewGuid().ToString("N");
        bool active = false;
        bool conflictActive = false;
        bool createActive = false;
        AssetDatabase.CreateFolder("Assets", Path.GetFileName(folder));
        string path = folder + "/Live.tiff";
        try
        {
            string create = WhimTexApi.ExecuteJson("{\"apiVersion\":1,\"assetPath\":\"" + path + "\",\"create\":true,\"width\":32,\"height\":32,\"operations\":[]}");
            Check(create.Contains("\"success\":true"), "seed TIFF");
            string inspect = WhimTexApi.Inspect(path);
            string revision = Revision(inspect);
            string begin = WhimTexApi.TiffLiveJson("{\"apiVersion\":1,\"op\":\"begin\",\"sessionId\":\"" + sessionId + "\",\"assetPath\":\"" + path + "\",\"expectedRevision\":\"" + revision + "\"}");
            Check(begin.Contains("\"success\":true") && begin.Contains("\"state\":\"pending\""), "begin independent TIFF session");
            active = true;
            string listed = WhimTexApi.TiffLiveJson("{\"apiVersion\":1,\"op\":\"list\"}");
            Check(listed.Contains(sessionId), "list discovers independent TIFF session");
            string operations = "[{\"op\":\"add\",\"type\":\"color\",\"as\":\"live\",\"settings\":{\"name\":\"Live Overlay\",\"color\":[0.9,0.2,0.1,1]}}]";
            string previewResult = WhimTexApi.TiffLiveJson("{\"apiVersion\":1,\"op\":\"preview\",\"sessionId\":\"" + sessionId + "\",\"requestId\":\"preview-1\",\"operations\":" + operations + "}");
            Check(previewResult.Contains("\"success\":true") && previewResult.Contains("Live Overlay") && previewResult.Contains("\"phase\":\"preview\""), "preview updates transient model");
            string status = WhimTexApi.TiffLiveJson("{\"apiVersion\":1,\"op\":\"status\",\"sessionId\":\"" + sessionId + "\"}");
            Check(status.Contains("Live Overlay"), "status exposes preview model");
            string render = WhimTexApi.TiffLiveJson("{\"apiVersion\":1,\"op\":\"render\",\"sessionId\":\"" + sessionId + "\",\"requestId\":\"render-1\",\"outputPath\":\"" + preview + "\"}");
            Check(render.Contains("\"success\":true") && File.Exists(previewFull), "render independent session preview");
            string completeRequest = "{\"apiVersion\":1,\"op\":\"complete\",\"sessionId\":\"" + sessionId + "\",\"requestId\":\"complete-1\",\"operations\":[]}";
            string complete = WhimTexApi.TiffLiveJson(completeRequest);
            Check(complete.Contains("\"success\":true") && complete.Contains("\"saved\":true") && complete.Contains("\"phase\":\"complete\""), "complete atomically saves TIFF");
            string completeRetry = WhimTexApi.TiffLiveJson(completeRequest);
            Check(completeRetry.Contains("\"success\":true") && completeRetry.Contains("\"replayed\":true"), "complete retry is idempotent after save");
            active = false;
            Check(WhimTexApi.Inspect(path).Contains("Live Overlay"), "completed TIFF persists preview model");
            string conflictRevision = Revision(WhimTexApi.Inspect(path));
            string conflictBegin = WhimTexApi.TiffLiveJson("{\"apiVersion\":1,\"op\":\"begin\",\"sessionId\":\"" + conflictSessionId + "\",\"assetPath\":\"" + path + "\",\"expectedRevision\":\"" + conflictRevision + "\"}");
            Check(conflictBegin.Contains("\"success\":true"), "begin conflict-check session");
            conflictActive = true;
            TextureCompositor externalDocument = WhimTexDocumentFile.Load(path);
            try
            {
                externalDocument.layers.Add(new Layer(new ColorFillLayerBehaviour { color = Color.green }));
                WhimTexDocumentFile.Save(externalDocument, path);
            }
            finally { UnityEngine.Object.DestroyImmediate(externalDocument); }
            string conflictComplete = WhimTexApi.TiffLiveJson("{\"apiVersion\":1,\"op\":\"complete\",\"sessionId\":\"" + conflictSessionId + "\",\"operations\":[]}");
            Check(conflictComplete.Contains("\"success\":false") && conflictComplete.Contains("revision_conflict"), "complete rejects external TIFF change");
            string cancelRequest = "{\"apiVersion\":1,\"op\":\"cancel\",\"sessionId\":\"" + conflictSessionId + "\",\"requestId\":\"cancel-1\"}";
            string cancel = WhimTexApi.TiffLiveJson(cancelRequest);
            Check(cancel.Contains("\"success\":true") && cancel.Contains("\"phase\":\"cancel\""), "cancel closes conflict session");
            string cancelRetry = WhimTexApi.TiffLiveJson(cancelRequest);
            Check(cancelRetry.Contains("\"success\":true") && cancelRetry.Contains("\"replayed\":true"), "cancel retry is idempotent");
            conflictActive = false;
            string createdPath = folder + "/Created.tiff";
            string createBegin = WhimTexApi.TiffLiveJson("{\"apiVersion\":1,\"op\":\"begin\",\"sessionId\":\"" + createSessionId + "\",\"assetPath\":\"" + createdPath + "\",\"create\":true,\"width\":16,\"height\":16}");
            Check(createBegin.Contains("\"success\":true"), "begin new TIFF live session");
            createActive = true;
            string createPreview = WhimTexApi.TiffLiveJson("{\"apiVersion\":1,\"op\":\"preview\",\"sessionId\":\"" + createSessionId + "\",\"operations\":[{\"op\":\"add\",\"type\":\"color\",\"settings\":{\"name\":\"Created Live\",\"color\":[0.1,0.4,1,1]}}]}");
            Check(createPreview.Contains("\"success\":true"), "preview new TIFF live session");
            string createComplete = WhimTexApi.TiffLiveJson("{\"apiVersion\":1,\"op\":\"complete\",\"sessionId\":\"" + createSessionId + "\",\"requestId\":\"create-complete-1\",\"operations\":[]}");
            Check(createComplete.Contains("\"success\":true") && File.Exists(Path.Combine(projectRoot, createdPath)), "complete creates new TIFF");
            createActive = false;
            return "PASS: independent TIFF live begin, preview, status, render, complete, and persistence.";
        }
        finally
        {
            if (active) { try { WhimTexApi.TiffLiveJson("{\"apiVersion\":1,\"op\":\"cancel\",\"sessionId\":\"" + sessionId + "\"}"); } catch { } }
            if (conflictActive) { try { WhimTexApi.TiffLiveJson("{\"apiVersion\":1,\"op\":\"cancel\",\"sessionId\":\"" + conflictSessionId + "\"}"); } catch { } }
            if (createActive) { try { WhimTexApi.TiffLiveJson("{\"apiVersion\":1,\"op\":\"cancel\",\"sessionId\":\"" + createSessionId + "\"}"); } catch { } }
            if (File.Exists(previewFull)) File.Delete(previewFull);
            AssetDatabase.DeleteAsset(folder);
        }
    }
}
