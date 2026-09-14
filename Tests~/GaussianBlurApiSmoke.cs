// Opt-in after manual compilation. No imports, saves, rendering or Undo.
var type=typeof(DCFApixels.WhimTex.WhimTexApi);
var flags=System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic;
var setter=type.GetMethod("SetBlur",flags);
var snapshot=type.GetMethod("BlurSnapshot",flags);
var jsonType=setter.GetParameters()[1].ParameterType;
object Json(string text)=>jsonType.GetMethod("Parse",new[]{typeof(string)}).Invoke(null,new object[]{text});
var layer=new DCFApixels.WhimTex.BlurLayerBehaviour();int checks=0;
void Check(bool value,string message){if(!value)throw new System.Exception(message);checks++;}
void Set(string json)=>setter.Invoke(null,new object[]{layer,Json(json)});
void Reject(string json)
{
    bool rejected=false;try{Set(json);}catch(System.Reflection.TargetInvocationException e){rejected=e.InnerException?.GetType().Name=="WhimTexApiException";}
    Check(rejected,"Reject "+json);
}
Check(layer.strength==1 && layer.radius==8 && layer.edges==DCFApixels.WhimTex.BlurLayerBehaviour.EdgeMode.Transparent,"Defaults");
Set("{\"radius\":32.5,\"edges\":\"Repeat\"}");Check(layer.radius==32.5f && layer.edges==DCFApixels.WhimTex.BlurLayerBehaviour.EdgeMode.Repeat,"Set parameters");
Check(layer.strength==1,"Omitted strength preserves default");
Set("{\"strength\":2.5}");Check(layer.strength==2.5f && layer.radius==32.5f,"Strength partial update");
var copy=new DCFApixels.WhimTex.BlurLayerBehaviour();setter.Invoke(null,new object[]{copy,snapshot.Invoke(null,new object[]{layer})});
Check(snapshot.Invoke(null,new object[]{layer}).ToString()==snapshot.Invoke(null,new object[]{copy}).ToString(),"Settings round trip");
Reject("{\"radius\":-1}");Reject("{\"radius\":257}");Reject("{\"edges\":\"Unknown\"}");Reject("{\"unused\":true}");
Reject("{\"strength\":-0.1}");Reject("{\"strength\":4.1}");Reject("{\"strength\":null}");
Check(DCFApixels.WhimTex.WhimTexApi.Describe().Contains("blurDefaults"),"Discovery");
return "Gaussian API checks passed: "+checks;
