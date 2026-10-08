// Independent migrated assertions; compiled and executed only by the parent runner.
using WhimTex.Tests;
using WhimTex.Tests.UnityD;
using System;
using System.Collections;
using System.Reflection;
using UnityEngine;
using DCFApixels.WhimTex;

public static class WhimTexGradientRetiredFieldTests
{
    static TestContext context;
    static MigrationD fixture;

    public static string Run() => TestContext.Run("WhimTexGradientRetiredFieldTests.Run", runContext =>
    {
        context = runContext;
        using (fixture = new MigrationD()) ExecuteMain();
    });

    // Pre-0.12.5 snapshots (IDs 0..5): unknown fields must be diagnosed, not silently discarded.
    static readonly string[] Fixtures = {
        "AQAAAB0iRENGQXBpeGVscy5XaGltVGV4LldoaW1UZXhHcmFkaWVudAcAAAAGY29sb3JzHgMAAAAdLERDRkFwaXhlbHMuV2hpbVRleC5XaGltVGV4R3JhZGllbnQrQ29sb3JTdG9wAwAAAAVjb2xvchUAAIA/AACAPwAAgD8AAIA/BHRpbWUKzcxMPghtaWRwb2ludAqamZk+HSxEQ0ZBcGl4ZWxzLldoaW1UZXguV2hpbVRleEdyYWRpZW50K0NvbG9yU3RvcAMAAAAFY29sb3IVAAAAAAAAAAAAAIA/AACAPwR0aW1lCs3MDD8IbWlkcG9pbnQKAAAAPx0sRENGQXBpeGVscy5XaGltVGV4LldoaW1UZXhHcmFkaWVudCtDb2xvclN0b3ADAAAABWNvbG9yFQAAgD8AAAAAAAAAAAAAgD8EdGltZQoAAIA/CG1pZHBvaW50CgAAAD8GYWxwaGFzHgIAAAAdLERDRkFwaXhlbHMuV2hpbVRleC5XaGltVGV4R3JhZGllbnQrQWxwaGFTdG9wAwAAAAVhbHBoYQqamZk+BHRpbWUKzczMPQhtaWRwb2ludAoAAAA/HSxEQ0ZBcGl4ZWxzLldoaW1UZXguV2hpbVRleEdyYWRpZW50K0FscGhhU3RvcAMAAAAFYWxwaGEKzcxMPwR0aW1lCmZmZj8IbWlkcG9pbnQKAAAAPwRtb2RlDiZEQ0ZBcGl4ZWxzLldoaW1UZXguV2hpbVRleEdyYWRpZW50TW9kZQpQZXJjZXB0dWFsBQAAAAAAAAAId3JhcE1vZGUOKkRDRkFwaXhlbHMuV2hpbVRleC5XaGltVGV4R3JhZGllbnRXcmFwTW9kZQVDbGFtcAAAAAAAAAAACmNvbG9yU3BhY2UOFlVuaXR5RW5naW5lLkNvbG9yU3BhY2UFR2FtbWEAAAAAAAAAAApzbW9vdGhuZXNzCgAAgD8KdHJhbnNpdGlvbg4sRENGQXBpeGVscy5XaGltVGV4LldoaW1UZXhHcmFkaWVudFRyYW5zaXRpb24BMAAAAAAAAAAA",
        "AQAAAB0iRENGQXBpeGVscy5XaGltVGV4LldoaW1UZXhHcmFkaWVudAcAAAAGY29sb3JzHgMAAAAdLERDRkFwaXhlbHMuV2hpbVRleC5XaGltVGV4R3JhZGllbnQrQ29sb3JTdG9wAwAAAAVjb2xvchUAAIA/AACAPwAAgD8AAIA/BHRpbWUKzcxMPghtaWRwb2ludAqamZk+HSxEQ0ZBcGl4ZWxzLldoaW1UZXguV2hpbVRleEdyYWRpZW50K0NvbG9yU3RvcAMAAAAFY29sb3IVAAAAAAAAAAAAAIA/AACAPwR0aW1lCs3MDD8IbWlkcG9pbnQKAAAAPx0sRENGQXBpeGVscy5XaGltVGV4LldoaW1UZXhHcmFkaWVudCtDb2xvclN0b3ADAAAABWNvbG9yFQAAgD8AAAAAAAAAAAAAgD8EdGltZQoAAIA/CG1pZHBvaW50CgAAAD8GYWxwaGFzHgIAAAAdLERDRkFwaXhlbHMuV2hpbVRleC5XaGltVGV4R3JhZGllbnQrQWxwaGFTdG9wAwAAAAVhbHBoYQqamZk+BHRpbWUKzczMPQhtaWRwb2ludAoAAAA/HSxEQ0ZBcGl4ZWxzLldoaW1UZXguV2hpbVRleEdyYWRpZW50K0FscGhhU3RvcAMAAAAFYWxwaGEKzcxMPwR0aW1lCmZmZj8IbWlkcG9pbnQKAAAAPwRtb2RlDiZEQ0ZBcGl4ZWxzLldoaW1UZXguV2hpbVRleEdyYWRpZW50TW9kZQpQZXJjZXB0dWFsBQAAAAAAAAAId3JhcE1vZGUOKkRDRkFwaXhlbHMuV2hpbVRleC5XaGltVGV4R3JhZGllbnRXcmFwTW9kZQVDbGFtcAAAAAAAAAAACmNvbG9yU3BhY2UOFlVuaXR5RW5naW5lLkNvbG9yU3BhY2UFR2FtbWEAAAAAAAAAAApzbW9vdGhuZXNzCgAAgD8KdHJhbnNpdGlvbg4sRENGQXBpeGVscy5XaGltVGV4LldoaW1UZXhHcmFkaWVudFRyYW5zaXRpb24BMQEAAAAAAAAA",
        "AQAAAB0iRENGQXBpeGVscy5XaGltVGV4LldoaW1UZXhHcmFkaWVudAcAAAAGY29sb3JzHgMAAAAdLERDRkFwaXhlbHMuV2hpbVRleC5XaGltVGV4R3JhZGllbnQrQ29sb3JTdG9wAwAAAAVjb2xvchUAAIA/AACAPwAAgD8AAIA/BHRpbWUKzcxMPghtaWRwb2ludAqamZk+HSxEQ0ZBcGl4ZWxzLldoaW1UZXguV2hpbVRleEdyYWRpZW50K0NvbG9yU3RvcAMAAAAFY29sb3IVAAAAAAAAAAAAAIA/AACAPwR0aW1lCs3MDD8IbWlkcG9pbnQKAAAAPx0sRENGQXBpeGVscy5XaGltVGV4LldoaW1UZXhHcmFkaWVudCtDb2xvclN0b3ADAAAABWNvbG9yFQAAgD8AAAAAAAAAAAAAgD8EdGltZQoAAIA/CG1pZHBvaW50CgAAAD8GYWxwaGFzHgIAAAAdLERDRkFwaXhlbHMuV2hpbVRleC5XaGltVGV4R3JhZGllbnQrQWxwaGFTdG9wAwAAAAVhbHBoYQqamZk+BHRpbWUKzczMPQhtaWRwb2ludAoAAAA/HSxEQ0ZBcGl4ZWxzLldoaW1UZXguV2hpbVRleEdyYWRpZW50K0FscGhhU3RvcAMAAAAFYWxwaGEKzcxMPwR0aW1lCmZmZj8IbWlkcG9pbnQKAAAAPwRtb2RlDiZEQ0ZBcGl4ZWxzLldoaW1UZXguV2hpbVRleEdyYWRpZW50TW9kZQpQZXJjZXB0dWFsBQAAAAAAAAAId3JhcE1vZGUOKkRDRkFwaXhlbHMuV2hpbVRleC5XaGltVGV4R3JhZGllbnRXcmFwTW9kZQVDbGFtcAAAAAAAAAAACmNvbG9yU3BhY2UOFlVuaXR5RW5naW5lLkNvbG9yU3BhY2UFR2FtbWEAAAAAAAAAAApzbW9vdGhuZXNzCgAAgD8KdHJhbnNpdGlvbg4sRENGQXBpeGVscy5XaGltVGV4LldoaW1UZXhHcmFkaWVudFRyYW5zaXRpb24BMgIAAAAAAAAA",
        "AQAAAB0iRENGQXBpeGVscy5XaGltVGV4LldoaW1UZXhHcmFkaWVudAcAAAAGY29sb3JzHgMAAAAdLERDRkFwaXhlbHMuV2hpbVRleC5XaGltVGV4R3JhZGllbnQrQ29sb3JTdG9wAwAAAAVjb2xvchUAAIA/AACAPwAAgD8AAIA/BHRpbWUKzcxMPghtaWRwb2ludAqamZk+HSxEQ0ZBcGl4ZWxzLldoaW1UZXguV2hpbVRleEdyYWRpZW50K0NvbG9yU3RvcAMAAAAFY29sb3IVAAAAAAAAAAAAAIA/AACAPwR0aW1lCs3MDD8IbWlkcG9pbnQKAAAAPx0sRENGQXBpeGVscy5XaGltVGV4LldoaW1UZXhHcmFkaWVudCtDb2xvclN0b3ADAAAABWNvbG9yFQAAgD8AAAAAAAAAAAAAgD8EdGltZQoAAIA/CG1pZHBvaW50CgAAAD8GYWxwaGFzHgIAAAAdLERDRkFwaXhlbHMuV2hpbVRleC5XaGltVGV4R3JhZGllbnQrQWxwaGFTdG9wAwAAAAVhbHBoYQqamZk+BHRpbWUKzczMPQhtaWRwb2ludAoAAAA/HSxEQ0ZBcGl4ZWxzLldoaW1UZXguV2hpbVRleEdyYWRpZW50K0FscGhhU3RvcAMAAAAFYWxwaGEKzcxMPwR0aW1lCmZmZj8IbWlkcG9pbnQKAAAAPwRtb2RlDiZEQ0ZBcGl4ZWxzLldoaW1UZXguV2hpbVRleEdyYWRpZW50TW9kZQpQZXJjZXB0dWFsBQAAAAAAAAAId3JhcE1vZGUOKkRDRkFwaXhlbHMuV2hpbVRleC5XaGltVGV4R3JhZGllbnRXcmFwTW9kZQVDbGFtcAAAAAAAAAAACmNvbG9yU3BhY2UOFlVuaXR5RW5naW5lLkNvbG9yU3BhY2UFR2FtbWEAAAAAAAAAAApzbW9vdGhuZXNzCgAAgD8KdHJhbnNpdGlvbg4sRENGQXBpeGVscy5XaGltVGV4LldoaW1UZXhHcmFkaWVudFRyYW5zaXRpb24BMwMAAAAAAAAA",
        "AQAAAB0iRENGQXBpeGVscy5XaGltVGV4LldoaW1UZXhHcmFkaWVudAcAAAAGY29sb3JzHgMAAAAdLERDRkFwaXhlbHMuV2hpbVRleC5XaGltVGV4R3JhZGllbnQrQ29sb3JTdG9wAwAAAAVjb2xvchUAAIA/AACAPwAAgD8AAIA/BHRpbWUKzcxMPghtaWRwb2ludAqamZk+HSxEQ0ZBcGl4ZWxzLldoaW1UZXguV2hpbVRleEdyYWRpZW50K0NvbG9yU3RvcAMAAAAFY29sb3IVAAAAAAAAAAAAAIA/AACAPwR0aW1lCs3MDD8IbWlkcG9pbnQKAAAAPx0sRENGQXBpeGVscy5XaGltVGV4LldoaW1UZXhHcmFkaWVudCtDb2xvclN0b3ADAAAABWNvbG9yFQAAgD8AAAAAAAAAAAAAgD8EdGltZQoAAIA/CG1pZHBvaW50CgAAAD8GYWxwaGFzHgIAAAAdLERDRkFwaXhlbHMuV2hpbVRleC5XaGltVGV4R3JhZGllbnQrQWxwaGFTdG9wAwAAAAVhbHBoYQqamZk+BHRpbWUKzczMPQhtaWRwb2ludAoAAAA/HSxEQ0ZBcGl4ZWxzLldoaW1UZXguV2hpbVRleEdyYWRpZW50K0FscGhhU3RvcAMAAAAFYWxwaGEKzcxMPwR0aW1lCmZmZj8IbWlkcG9pbnQKAAAAPwRtb2RlDiZEQ0ZBcGl4ZWxzLldoaW1UZXguV2hpbVRleEdyYWRpZW50TW9kZQpQZXJjZXB0dWFsBQAAAAAAAAAId3JhcE1vZGUOKkRDRkFwaXhlbHMuV2hpbVRleC5XaGltVGV4R3JhZGllbnRXcmFwTW9kZQVDbGFtcAAAAAAAAAAACmNvbG9yU3BhY2UOFlVuaXR5RW5naW5lLkNvbG9yU3BhY2UFR2FtbWEAAAAAAAAAAApzbW9vdGhuZXNzCgAAgD8KdHJhbnNpdGlvbg4sRENGQXBpeGVscy5XaGltVGV4LldoaW1UZXhHcmFkaWVudFRyYW5zaXRpb24BNAQAAAAAAAAA",
        "AQAAAB0iRENGQXBpeGVscy5XaGltVGV4LldoaW1UZXhHcmFkaWVudAcAAAAGY29sb3JzHgMAAAAdLERDRkFwaXhlbHMuV2hpbVRleC5XaGltVGV4R3JhZGllbnQrQ29sb3JTdG9wAwAAAAVjb2xvchUAAIA/AACAPwAAgD8AAIA/BHRpbWUKzcxMPghtaWRwb2ludAqamZk+HSxEQ0ZBcGl4ZWxzLldoaW1UZXguV2hpbVRleEdyYWRpZW50K0NvbG9yU3RvcAMAAAAFY29sb3IVAAAAAAAAAAAAAIA/AACAPwR0aW1lCs3MDD8IbWlkcG9pbnQKAAAAPx0sRENGQXBpeGVscy5XaGltVGV4LldoaW1UZXhHcmFkaWVudCtDb2xvclN0b3ADAAAABWNvbG9yFQAAgD8AAAAAAAAAAAAAgD8EdGltZQoAAIA/CG1pZHBvaW50CgAAAD8GYWxwaGFzHgIAAAAdLERDRkFwaXhlbHMuV2hpbVRleC5XaGltVGV4R3JhZGllbnQrQWxwaGFTdG9wAwAAAAVhbHBoYQqamZk+BHRpbWUKzczMPQhtaWRwb2ludAoAAAA/HSxEQ0ZBcGl4ZWxzLldoaW1UZXguV2hpbVRleEdyYWRpZW50K0FscGhhU3RvcAMAAAAFYWxwaGEKzcxMPwR0aW1lCmZmZj8IbWlkcG9pbnQKAAAAPwRtb2RlDiZEQ0ZBcGl4ZWxzLldoaW1UZXguV2hpbVRleEdyYWRpZW50TW9kZQpQZXJjZXB0dWFsBQAAAAAAAAAId3JhcE1vZGUOKkRDRkFwaXhlbHMuV2hpbVRleC5XaGltVGV4R3JhZGllbnRXcmFwTW9kZQVDbGFtcAAAAAAAAAAACmNvbG9yU3BhY2UOFlVuaXR5RW5naW5lLkNvbG9yU3BhY2UFR2FtbWEAAAAAAAAAAApzbW9vdGhuZXNzCgAAgD8KdHJhbnNpdGlvbg4sRENGQXBpeGVscy5XaGltVGV4LldoaW1UZXhHcmFkaWVudFRyYW5zaXRpb24HUm91bmRlZAUAAAAAAAAA"
    };
    private static void ExecuteMain()
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
            var read=ser.GetMethod("Deserialize",flags).Invoke(null,new object[] {bytes,container,typeof(WhimTexGradient),null,false});
            var actual=(WhimTexGradient)read.GetType().GetProperty("Model", BindingFlags.Instance|BindingFlags.NonPublic).GetValue(read);
            context.True(!(!actual.Equals(expected)||actual.GetHashCode()!=expected.GetHashCode()), "Retired field affected values/identity");
            checks++;
            foreach(string name in new[] {"SkippedFields","MissingTypes"})
            {
                var diagnostics = (ICollection)read.GetType().GetProperty(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(read);
                if (name == "SkippedFields")
                {
                    context.True(!(diagnostics.Count != 1), "Unknown transition was not diagnosed");
                    foreach (object label in diagnostics)
                        context.True(!((string)label != "WhimTexGradient.transition"), "Wrong unknown-field diagnostic");
                }
                else context.True(!(diagnostics.Count != 0), "Unexpected load diagnostic: " + name);
                checks++;
            }
            for(int i=0;i<=1024;i++)
            {
                context.True(!(!actual.Evaluate(i/1024f).Equals(expected.Evaluate(i/1024f))), "Retired field affected rendering");
                checks++;
            }
            byte[] saved=(byte[])ser.GetMethod("Serialize",flags).Invoke(null,new object[] {actual,container});
            context.True(!(System.Text.Encoding.UTF8.GetString(saved).Contains("transition")), "Writer retained retired field");
            checks++;
        }
        return;
    }
}

