using System;
using System.Collections;
using System.Reflection;
using UnityEngine;
using DCFApixels.WhimTex;

public static class WhimTexGradientRetiredFieldSmoke
{
    // Captured with the previous document writer before removing transition (IDs 0..5).
    static readonly string[] Fixtures = {
        "AQAAAB0iRENGQXBpeGVscy5XaGltVGV4LldoaW1UZXhHcmFkaWVudAcAAAAGY29sb3JzHgMAAAAdLERDRkFwaXhlbHMuV2hpbVRleC5XaGltVGV4R3JhZGllbnQrQ29sb3JTdG9wAwAAAAVjb2xvchUAAIA/AACAPwAAgD8AAIA/BHRpbWUKzcxMPghtaWRwb2ludAqamZk+HSxEQ0ZBcGl4ZWxzLldoaW1UZXguV2hpbVRleEdyYWRpZW50K0NvbG9yU3RvcAMAAAAFY29sb3IVAAAAAAAAAAAAAIA/AACAPwR0aW1lCs3MDD8IbWlkcG9pbnQKAAAAPx0sRENGQXBpeGVscy5XaGltVGV4LldoaW1UZXhHcmFkaWVudCtDb2xvclN0b3ADAAAABWNvbG9yFQAAgD8AAAAAAAAAAAAAgD8EdGltZQoAAIA/CG1pZHBvaW50CgAAAD8GYWxwaGFzHgIAAAAdLERDRkFwaXhlbHMuV2hpbVRleC5XaGltVGV4R3JhZGllbnQrQWxwaGFTdG9wAwAAAAVhbHBoYQqamZk+BHRpbWUKzczMPQhtaWRwb2ludAoAAAA/HSxEQ0ZBcGl4ZWxzLldoaW1UZXguV2hpbVRleEdyYWRpZW50K0FscGhhU3RvcAMAAAAFYWxwaGEKzcxMPwR0aW1lCmZmZj8IbWlkcG9pbnQKAAAAPwRtb2RlDiZEQ0ZBcGl4ZWxzLldoaW1UZXguV2hpbVRleEdyYWRpZW50TW9kZQpQZXJjZXB0dWFsBQAAAAAAAAAId3JhcE1vZGUOKkRDRkFwaXhlbHMuV2hpbVRleC5XaGltVGV4R3JhZGllbnRXcmFwTW9kZQVDbGFtcAAAAAAAAAAACmNvbG9yU3BhY2UOFlVuaXR5RW5naW5lLkNvbG9yU3BhY2UFR2FtbWEAAAAAAAAAAApzbW9vdGhuZXNzCgAAgD8KdHJhbnNpdGlvbg4sRENGQXBpeGVscy5XaGltVGV4LldoaW1UZXhHcmFkaWVudFRyYW5zaXRpb24BMAAAAAAAAAAA",
        "AQAAAB0iRENGQXBpeGVscy5XaGltVGV4LldoaW1UZXhHcmFkaWVudAcAAAAGY29sb3JzHgMAAAAdLERDRkFwaXhlbHMuV2hpbVRleC5XaGltVGV4R3JhZGllbnQrQ29sb3JTdG9wAwAAAAVjb2xvchUAAIA/AACAPwAAgD8AAIA/BHRpbWUKzcxMPghtaWRwb2ludAqamZk+HSxEQ0ZBcGl4ZWxzLldoaW1UZXguV2hpbVRleEdyYWRpZW50K0NvbG9yU3RvcAMAAAAFY29sb3IVAAAAAAAAAAAAAIA/AACAPwR0aW1lCs3MDD8IbWlkcG9pbnQKAAAAPx0sRENGQXBpeGVscy5XaGltVGV4LldoaW1UZXhHcmFkaWVudCtDb2xvclN0b3ADAAAABWNvbG9yFQAAgD8AAAAAAAAAAAAAgD8EdGltZQoAAIA/CG1pZHBvaW50CgAAAD8GYWxwaGFzHgIAAAAdLERDRkFwaXhlbHMuV2hpbVRleC5XaGltVGV4R3JhZGllbnQrQWxwaGFTdG9wAwAAAAVhbHBoYQqamZk+BHRpbWUKzczMPQhtaWRwb2ludAoAAAA/HSxEQ0ZBcGl4ZWxzLldoaW1UZXguV2hpbVRleEdyYWRpZW50K0FscGhhU3RvcAMAAAAFYWxwaGEKzcxMPwR0aW1lCmZmZj8IbWlkcG9pbnQKAAAAPwRtb2RlDiZEQ0ZBcGl4ZWxzLldoaW1UZXguV2hpbVRleEdyYWRpZW50TW9kZQpQZXJjZXB0dWFsBQAAAAAAAAAId3JhcE1vZGUOKkRDRkFwaXhlbHMuV2hpbVRleC5XaGltVGV4R3JhZGllbnRXcmFwTW9kZQVDbGFtcAAAAAAAAAAACmNvbG9yU3BhY2UOFlVuaXR5RW5naW5lLkNvbG9yU3BhY2UFR2FtbWEAAAAAAAAAAApzbW9vdGhuZXNzCgAAgD8KdHJhbnNpdGlvbg4sRENGQXBpeGVscy5XaGltVGV4LldoaW1UZXhHcmFkaWVudFRyYW5zaXRpb24BMQEAAAAAAAAA",
        "AQAAAB0iRENGQXBpeGVscy5XaGltVGV4LldoaW1UZXhHcmFkaWVudAcAAAAGY29sb3JzHgMAAAAdLERDRkFwaXhlbHMuV2hpbVRleC5XaGltVGV4R3JhZGllbnQrQ29sb3JTdG9wAwAAAAVjb2xvchUAAIA/AACAPwAAgD8AAIA/BHRpbWUKzcxMPghtaWRwb2ludAqamZk+HSxEQ0ZBcGl4ZWxzLldoaW1UZXguV2hpbVRleEdyYWRpZW50K0NvbG9yU3RvcAMAAAAFY29sb3IVAAAAAAAAAAAAAIA/AACAPwR0aW1lCs3MDD8IbWlkcG9pbnQKAAAAPx0sRENGQXBpeGVscy5XaGltVGV4LldoaW1UZXhHcmFkaWVudCtDb2xvclN0b3ADAAAABWNvbG9yFQAAgD8AAAAAAAAAAAAAgD8EdGltZQoAAIA/CG1pZHBvaW50CgAAAD8GYWxwaGFzHgIAAAAdLERDRkFwaXhlbHMuV2hpbVRleC5XaGltVGV4R3JhZGllbnQrQWxwaGFTdG9wAwAAAAVhbHBoYQqamZk+BHRpbWUKzczMPQhtaWRwb2ludAoAAAA/HSxEQ0ZBcGl4ZWxzLldoaW1UZXguV2hpbVRleEdyYWRpZW50K0FscGhhU3RvcAMAAAAFYWxwaGEKzcxMPwR0aW1lCmZmZj8IbWlkcG9pbnQKAAAAPwRtb2RlDiZEQ0ZBcGl4ZWxzLldoaW1UZXguV2hpbVRleEdyYWRpZW50TW9kZQpQZXJjZXB0dWFsBQAAAAAAAAAId3JhcE1vZGUOKkRDRkFwaXhlbHMuV2hpbVRleC5XaGltVGV4R3JhZGllbnRXcmFwTW9kZQVDbGFtcAAAAAAAAAAACmNvbG9yU3BhY2UOFlVuaXR5RW5naW5lLkNvbG9yU3BhY2UFR2FtbWEAAAAAAAAAAApzbW9vdGhuZXNzCgAAgD8KdHJhbnNpdGlvbg4sRENGQXBpeGVscy5XaGltVGV4LldoaW1UZXhHcmFkaWVudFRyYW5zaXRpb24BMgIAAAAAAAAA",
        "AQAAAB0iRENGQXBpeGVscy5XaGltVGV4LldoaW1UZXhHcmFkaWVudAcAAAAGY29sb3JzHgMAAAAdLERDRkFwaXhlbHMuV2hpbVRleC5XaGltVGV4R3JhZGllbnQrQ29sb3JTdG9wAwAAAAVjb2xvchUAAIA/AACAPwAAgD8AAIA/BHRpbWUKzcxMPghtaWRwb2ludAqamZk+HSxEQ0ZBcGl4ZWxzLldoaW1UZXguV2hpbVRleEdyYWRpZW50K0NvbG9yU3RvcAMAAAAFY29sb3IVAAAAAAAAAAAAAIA/AACAPwR0aW1lCs3MDD8IbWlkcG9pbnQKAAAAPx0sRENGQXBpeGVscy5XaGltVGV4LldoaW1UZXhHcmFkaWVudCtDb2xvclN0b3ADAAAABWNvbG9yFQAAgD8AAAAAAAAAAAAAgD8EdGltZQoAAIA/CG1pZHBvaW50CgAAAD8GYWxwaGFzHgIAAAAdLERDRkFwaXhlbHMuV2hpbVRleC5XaGltVGV4R3JhZGllbnQrQWxwaGFTdG9wAwAAAAVhbHBoYQqamZk+BHRpbWUKzczMPQhtaWRwb2ludAoAAAA/HSxEQ0ZBcGl4ZWxzLldoaW1UZXguV2hpbVRleEdyYWRpZW50K0FscGhhU3RvcAMAAAAFYWxwaGEKzcxMPwR0aW1lCmZmZj8IbWlkcG9pbnQKAAAAPwRtb2RlDiZEQ0ZBcGl4ZWxzLldoaW1UZXguV2hpbVRleEdyYWRpZW50TW9kZQpQZXJjZXB0dWFsBQAAAAAAAAAId3JhcE1vZGUOKkRDRkFwaXhlbHMuV2hpbVRleC5XaGltVGV4R3JhZGllbnRXcmFwTW9kZQVDbGFtcAAAAAAAAAAACmNvbG9yU3BhY2UOFlVuaXR5RW5naW5lLkNvbG9yU3BhY2UFR2FtbWEAAAAAAAAAAApzbW9vdGhuZXNzCgAAgD8KdHJhbnNpdGlvbg4sRENGQXBpeGVscy5XaGltVGV4LldoaW1UZXhHcmFkaWVudFRyYW5zaXRpb24BMwMAAAAAAAAA",
        "AQAAAB0iRENGQXBpeGVscy5XaGltVGV4LldoaW1UZXhHcmFkaWVudAcAAAAGY29sb3JzHgMAAAAdLERDRkFwaXhlbHMuV2hpbVRleC5XaGltVGV4R3JhZGllbnQrQ29sb3JTdG9wAwAAAAVjb2xvchUAAIA/AACAPwAAgD8AAIA/BHRpbWUKzcxMPghtaWRwb2ludAqamZk+HSxEQ0ZBcGl4ZWxzLldoaW1UZXguV2hpbVRleEdyYWRpZW50K0NvbG9yU3RvcAMAAAAFY29sb3IVAAAAAAAAAAAAAIA/AACAPwR0aW1lCs3MDD8IbWlkcG9pbnQKAAAAPx0sRENGQXBpeGVscy5XaGltVGV4LldoaW1UZXhHcmFkaWVudCtDb2xvclN0b3ADAAAABWNvbG9yFQAAgD8AAAAAAAAAAAAAgD8EdGltZQoAAIA/CG1pZHBvaW50CgAAAD8GYWxwaGFzHgIAAAAdLERDRkFwaXhlbHMuV2hpbVRleC5XaGltVGV4R3JhZGllbnQrQWxwaGFTdG9wAwAAAAVhbHBoYQqamZk+BHRpbWUKzczMPQhtaWRwb2ludAoAAAA/HSxEQ0ZBcGl4ZWxzLldoaW1UZXguV2hpbVRleEdyYWRpZW50K0FscGhhU3RvcAMAAAAFYWxwaGEKzcxMPwR0aW1lCmZmZj8IbWlkcG9pbnQKAAAAPwRtb2RlDiZEQ0ZBcGl4ZWxzLldoaW1UZXguV2hpbVRleEdyYWRpZW50TW9kZQpQZXJjZXB0dWFsBQAAAAAAAAAId3JhcE1vZGUOKkRDRkFwaXhlbHMuV2hpbVRleC5XaGltVGV4R3JhZGllbnRXcmFwTW9kZQVDbGFtcAAAAAAAAAAACmNvbG9yU3BhY2UOFlVuaXR5RW5naW5lLkNvbG9yU3BhY2UFR2FtbWEAAAAAAAAAAApzbW9vdGhuZXNzCgAAgD8KdHJhbnNpdGlvbg4sRENGQXBpeGVscy5XaGltVGV4LldoaW1UZXhHcmFkaWVudFRyYW5zaXRpb24BNAQAAAAAAAAA",
        "AQAAAB0iRENGQXBpeGVscy5XaGltVGV4LldoaW1UZXhHcmFkaWVudAcAAAAGY29sb3JzHgMAAAAdLERDRkFwaXhlbHMuV2hpbVRleC5XaGltVGV4R3JhZGllbnQrQ29sb3JTdG9wAwAAAAVjb2xvchUAAIA/AACAPwAAgD8AAIA/BHRpbWUKzcxMPghtaWRwb2ludAqamZk+HSxEQ0ZBcGl4ZWxzLldoaW1UZXguV2hpbVRleEdyYWRpZW50K0NvbG9yU3RvcAMAAAAFY29sb3IVAAAAAAAAAAAAAIA/AACAPwR0aW1lCs3MDD8IbWlkcG9pbnQKAAAAPx0sRENGQXBpeGVscy5XaGltVGV4LldoaW1UZXhHcmFkaWVudCtDb2xvclN0b3ADAAAABWNvbG9yFQAAgD8AAAAAAAAAAAAAgD8EdGltZQoAAIA/CG1pZHBvaW50CgAAAD8GYWxwaGFzHgIAAAAdLERDRkFwaXhlbHMuV2hpbVRleC5XaGltVGV4R3JhZGllbnQrQWxwaGFTdG9wAwAAAAVhbHBoYQqamZk+BHRpbWUKzczMPQhtaWRwb2ludAoAAAA/HSxEQ0ZBcGl4ZWxzLldoaW1UZXguV2hpbVRleEdyYWRpZW50K0FscGhhU3RvcAMAAAAFYWxwaGEKzcxMPwR0aW1lCmZmZj8IbWlkcG9pbnQKAAAAPwRtb2RlDiZEQ0ZBcGl4ZWxzLldoaW1UZXguV2hpbVRleEdyYWRpZW50TW9kZQpQZXJjZXB0dWFsBQAAAAAAAAAId3JhcE1vZGUOKkRDRkFwaXhlbHMuV2hpbVRleC5XaGltVGV4R3JhZGllbnRXcmFwTW9kZQVDbGFtcAAAAAAAAAAACmNvbG9yU3BhY2UOFlVuaXR5RW5naW5lLkNvbG9yU3BhY2UFR2FtbWEAAAAAAAAAAApzbW9vdGhuZXNzCgAAgD8KdHJhbnNpdGlvbg4sRENGQXBpeGVscy5XaGltVGV4LldoaW1UZXhHcmFkaWVudFRyYW5zaXRpb24HUm91bmRlZAUAAAAAAAAA"
    };
    public static string Main()
    {
        const BindingFlags flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static;
        var ser=typeof(WhimTexGradient).Assembly.GetType("DCFApixels.WhimTex.WhimTexDocumentSerializer");
        var expected=new WhimTexGradient {Mode=WhimTexGradientMode.Perceptual};
        expected.SetKeys(new[] {new GradientColorKey(Color.white,.2f),new GradientColorKey(Color.blue,.55f),new GradientColorKey(Color.red,1)},
            new[] {new GradientAlphaKey(.3f,.1f),new GradientAlphaKey(.8f,.9f)});
        expected.SetMidpoint(false,0,.3f);
        int checks=0;
        foreach(string fixture in Fixtures)
        using(var container=new WhimTexDocumentContainer())
        {
            byte[] bytes=Convert.FromBase64String(fixture);
            var actual=(WhimTexGradient)ser.GetMethod("Deserialize",flags).Invoke(null,new object[] {bytes,container,typeof(WhimTexGradient),null,false});
            if(!actual.Equals(expected)||actual.GetHashCode()!=expected.GetHashCode())throw new Exception("Retired field affected values/identity");
            checks++;
            foreach(string name in new[] {"LastSkippedFields","LastMissingTypes"})
            {
                if(((ICollection)ser.GetProperty(name,flags).GetValue(null)).Count!=0)throw new Exception("Unexpected load diagnostic: "+name);
                checks++;
            }
            for(int i=0;i<=1024;i++)
            {
                if(!actual.Evaluate(i/1024f).Equals(expected.Evaluate(i/1024f)))throw new Exception("Retired field affected rendering");
                checks++;
            }
            byte[] saved=(byte[])ser.GetMethod("Serialize",flags).Invoke(null,new object[] {actual,container});
            if(System.Text.Encoding.UTF8.GetString(saved).Contains("transition"))throw new Exception("Writer retained retired field");
            checks++;
        }
        return "Retired gradient field: "+checks+" checks passed; no migration or load warnings.";
    }
}
