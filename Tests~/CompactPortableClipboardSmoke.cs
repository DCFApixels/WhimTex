// Pipeline run_script entry CompactPortableClipboardSmoke.Run. Detached documents only.
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using DCFApixels.WhimTex;

public static class CompactPortableClipboardSmoke
{
    const BindingFlags F = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly Type Api = typeof(WhimTexApi);
    static readonly Type JsonObject = Type.GetType("Newtonsoft.Json.Linq.JObject, Newtonsoft.Json", true);
    static readonly Type JsonToken = Type.GetType("Newtonsoft.Json.Linq.JToken, Newtonsoft.Json", true);
    // Avoid a compile-time JSON reference: some projects load a second vendor copy of Newtonsoft.
    sealed class Json : System.Collections.Generic.IEnumerable<Json>
    {
        internal readonly object Value;
        internal Json(object value) { Value=value; }
        public Json this[object key]
        {
            get { var value=JsonToken.GetProperty("Item",new[]{typeof(object)}).GetValue(Value,new[]{key}); return value==null?null:new Json(value); }
            set => JsonToken.GetProperty("Item",new[]{typeof(object)}).SetValue(Value,value?.Value,new[]{key});
        }
        public int Count => (int)Value.GetType().GetProperty("Count").GetValue(Value);
        public bool IsArray => Value.GetType().Name=="JArray";
        public override string ToString() => Value.ToString();
        public static implicit operator Json(string value) => new Json(JsonToken.GetMethod("FromObject",new[]{typeof(object)}).Invoke(null,new object[]{value}));
        public static explicit operator string(Json value) => value?.ToString();
        public static explicit operator double(Json value) => double.Parse(value.ToString(),System.Globalization.CultureInfo.InvariantCulture);
        public IEnumerator<Json> GetEnumerator() { foreach(object value in (System.Collections.IEnumerable)Value)yield return new Json(value); }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()=>GetEnumerator();
    }
    static Json Parse(string json) => new Json(JsonObject.GetMethod("Parse", new[]{typeof(string)}).Invoke(null, new object[]{json}));
    static bool Equal(Json a, Json b) => (bool)JsonToken.GetMethod("DeepEquals", new[]{JsonToken,JsonToken}).Invoke(null,new[]{a?.Value,b?.Value});
    static int checks;
    static void Check(bool ok, string text) { if (!ok) throw new Exception(text); checks++; }
    static IDisposable Read(string json)
    {
        try { return (IDisposable)Api.GetMethod("ReadProceduralClipboard", F).Invoke(null, new object[] {json, 256, 256}); }
        catch(TargetInvocationException e) { throw new Exception(e.InnerException.ToString()); }
    }
    static TextureCompositor Doc(object data) => (TextureCompositor)data.GetType().GetField("Document", F).GetValue(data);
    static void Compile(object data) => data.GetType().GetMethod("Compile", F).Invoke(data, null);
    static string Write(TextureCompositor doc) => (string)Api.GetMethod("WritePortableClipboard", F).Invoke(null, new object[] {doc, doc.layers});
    static Json Snapshot(TextureCompositor doc) => new Json(Api.GetMethod("Snapshot", F).Invoke(null, new object[] {doc, ""}));
    static string Envelope(string layers) => "{'format':'whimtex.layers','version':1,'canvas':{'width':256,'height':256,'filter':'Point'},'layers':"+layers+"}";
    static string RoundTrip(string input, bool render = false)
    {
        using var source = Read(input); Compile(source);
        var document = Doc(source);
        string output = Write(document);
        using var restored = Read(output); Compile(restored);
        var copy = Doc(restored);
        Json a = Snapshot(document)["layers"], b = Snapshot(copy)["layers"];
        Check(a.Count==b.Count, "Layer count");
        for (int i=0;i<a.Count;i++)
        {
            foreach (string key in new[] {"type", "settings", "gradientKeys", "transform"})
                Check(Equal(a[i][key],b[i][key]), (string)a[i]["type"]+" changed "+key+"\n"+a[i][key]+"\n"+b[i][key]);
            Json ax=a[i]["fx"], bx=b[i]["fx"];
            Check(ax.Count==bx.Count, "FX count");
            for(int f=0;f<ax.Count;f++)
            {
                Check(Equal(ax[f]["enabled"],bx[f]["enabled"]), "FX visibility");
                Json pa=ax[f]["parameters"], pb=bx[f]["parameters"];
                Check(pa.Count==pb.Count, "Parameter count");
                for(int p=0;p<pa.Count;p++)
                {
                    Check(Equal(pa[p]["name"],pb[p]["name"]), "Parameter name");
                    // Layer texture IDs are remapped; PortableClipboardSmoke checks those bindings.
                    if((string)pa[p]["type"]!="Texture2D")
                        Check(Equal(pa[p]["value"],pb[p]["value"]), "Parameter value: "+pa[p]["name"]);
                }
            }
        }
        if(render)
        {
            Texture2D x=null,y=null;
            try
            {
                x=document.Compose(); y=copy.Compose();
                var xp=x.GetPixels();var yp=y.GetPixels(); double error=0;
                for(int i=0;i<xp.Length;i++)error+=Math.Abs(xp[i].r-yp[i].r)+Math.Abs(xp[i].g-yp[i].g)+Math.Abs(xp[i].b-yp[i].b)+Math.Abs(xp[i].a-yp[i].a);
                Check(error/(xp.Length*4)<.00001,"Render changed after compact copy: "+error/(xp.Length*4));
            }
            finally { if(x!=null)UnityEngine.Object.DestroyImmediate(x); if(y!=null)UnityEngine.Object.DestroyImmediate(y); }
        }
        return output;
    }
    public static string Run()
    {
        checks=0;
        string[] types={"drawing","file","color","gradient","noise","shape","outline","sdf","normalMap","blur","sharpen","makeSeamless","shaderProcessor","group"};
        var layers=new List<string>();foreach(string type in types)layers.Add("{'type':'"+type+"'}");
        layers.Add("{'type':'color'}");
        string compact=RoundTrip(Envelope("["+string.Join(",",layers)+"]"));
        Json result=Parse(compact);
        Check((string)result["canvas"]["filter"]=="Bilinear", "Explicit canvas filter is retained even when default");
        foreach(Json layer in result["layers"])
        {
            Check(layer["properties"]==null, "Default properties should disappear: "+layer);
            Check(layer["transform"]==null, "Default per-type transform should disappear: "+layer);
        }
        string minimal=RoundTrip(Envelope("[{'type':'color'}]"));
        Check(Parse(minimal)["layers"][0]["id"]==null,"Unreferenced IDs omitted");

        const string special = @"[
          { 'type':'color', 'properties':{'fillMode':'Color','fillPattern':{'gap':0.3,'size':[24,48],'palette':[{'time':0,'color':[1,0,0,1]},{'time':1,'color':[0,0,1,0]}]}} },
          { 'type':'shape', 'properties':{'opacity':0.999999,'enabled':false,'shape':{'roundness':0.2,'cornerRoundness':[0.2,0,0.4,0],'linkCorners':false}},'transform':{'pivot':[0,1],'position':[12,-4],'scale':[-1,2],'rotation':32} },
          { 'type':'gradient', 'properties':{'gradient':{'mode':'Linear','wrapMode':'Mirror','smoothness':0.6,'colorSpace':'Linear','colors':[{'time':0,'color':[0,0,0,1],'midpoint':0.7},{'time':1,'color':[2,1,0,1]}],'alphas':[{'time':0,'alpha':0.3,'midpoint':0.2},{'time':0.7,'alpha':1}]},'gradientOptions':{'type':'Radial','repetitions':3,'wrap':'PingPong'}},'transform':{'matrix':[1,0.2,0,0,1,0,0.1,0,1]} },
          { 'type':'noise','properties':{'noise':{'seed':731,'scale':12,'fractal':'FBm','octaves':4,'warpStrength':0.8},'swizzle':['G','R','B','1']} }
        ]";
        RoundTrip(Envelope(special),true);

        const string fxCode="// @param gradient _Tint\nfloat4 ApplyFX(float2 uv,float4 color){return _Tint_Sample(uv.x);}";
        Json fxNode=Parse("{'type':'color','name':'Ramp','fx':[{'name':'Ramp FX','code':''}]}");
        fxNode["fx"][0]["code"]=fxCode;
        Json fxOut=Parse(RoundTrip(Envelope("["+fxNode.ToString()+"]"),true));
        Check(fxOut["layers"][0]["fx"][0]["enabled"]==null,"Default FX visibility omitted");
        Check(fxOut["layers"][0]["fx"][0]["name"]==null,"Default FX name omitted");
        Check(fxOut["layers"][0]["fx"][0]["gradients"]==null,"Default FX gradient omitted");
        fxNode["fx"][0]["gradients"]=Parse(@"{'_Tint':[{'time':0,'color':[1,0,0,1]},{'time':0.4,'color':[0,1,0,0.5]},{'time':1,'color':[0,0,1,0]}]}");
        fxOut=Parse(RoundTrip(Envelope("["+fxNode.ToString()+"]"),true));
        Check(fxOut["layers"][0]["fx"][0]["gradients"]["_Tint"].IsArray,"Simple gradient metadata/derived alpha omitted");

        using(var linked=Read(Envelope(@"[{'type':'drawing','url':'https://example.com/test.png','transform':{'scale':[1,1]}}]")))
        {
            var drawing=(DrawingLayerBehaviour)Doc(linked).layers[0].Behaviour;
            var texture=new Texture2D(4,8);
            typeof(DrawingLayerBehaviour).GetMethod("AdoptStoredTexture",F).Invoke(drawing,new object[]{texture});
            typeof(DrawingLayerBehaviour).GetMethod("RememberImageUrl",F).Invoke(drawing,new object[]{"https://example.com/test.png"});
            Json json=Parse(Write(Doc(linked)));
            Check((double)json["layers"][0]["transform"]["scale"][0]==1 && (double)json["layers"][0]["transform"]["scale"][1]==1,"URL identity scale remains explicit");
        }
        long before=0,after=0;
        foreach(string file in Directory.GetFiles("Packages/com.dcfapixels.whimtex/Samples~/AgentTextures","*.layers.json"))
        {
            string input=File.ReadAllText(file);before+=input.Length;
            string output=RoundTrip(input,true);after+=output.Length;
        }
        return "PASS: "+checks+" compact export checks; all layer defaults, modified settings, transforms, FX gradients, URL scale and 12 sample renders. Sample JSON characters: "+before+" -> "+after+" (files not modified).";
    }
}
