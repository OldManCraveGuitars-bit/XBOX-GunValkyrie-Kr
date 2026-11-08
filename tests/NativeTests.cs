using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using GunValkyrieKorean;

public static class NativeTests
{
    private static string folder;
    private static byte[] source = Enumerable.Range(0, 8192).Select(i => (byte)(i % 251)).ToArray();
    private static int count;
    private static Action<string, int> quiet = (label, value) => { };

    private static byte[] Package(PatchSpec spec, Action<BinaryWriter> body)
    {
        using (MemoryStream output = new MemoryStream())
        {
            using (GZipStream gzip = new GZipStream(output, CompressionMode.Compress, true))
            using (BinaryWriter writer = new BinaryWriter(gzip))
            {
                writer.Write(Encoding.ASCII.GetBytes("GVKR0901"));
                writer.Write(spec.SourceBytes);
                writer.Write(spec.OutputBytes);
                writer.Write(FromHex(spec.SourceSha256));
                writer.Write(FromHex(spec.OutputSha256));
                body(writer);
            }
            byte[] bytes = output.ToArray();
            spec.PayloadSha256 = PatchEngine.Hash(bytes);
            return bytes;
        }
    }

    private static byte[] FromHex(string text)
    {
        return Enumerable.Range(0, text.Length / 2).Select(i => Convert.ToByte(text.Substring(i * 2, 2), 16)).ToArray();
    }

    private static byte[] Expected()
    {
        return source.Take(1000).Concat(Encoding.UTF8.GetBytes("한국어 literal"))
            .Concat(new byte[4000]).Concat(source.Skip(4000).Take(1500)).ToArray();
    }

    private static PatchSpec Spec()
    {
        return new PatchSpec { SourceBytes = source.Length, OutputBytes = Expected().Length,
            SourceSha256 = PatchEngine.Hash(source), OutputSha256 = PatchEngine.Hash(Expected()) };
    }

    private static byte[] GoodPayload(PatchSpec spec)
    {
        return Package(spec, writer => {
            writer.Write((byte)0); writer.Write((long)0); writer.Write((uint)1000);
            byte[] literal = Encoding.UTF8.GetBytes("한국어 literal");
            writer.Write((byte)1); writer.Write((uint)literal.Length); writer.Write(literal);
            writer.Write((byte)2); writer.Write((uint)4000);
            writer.Write((byte)0); writer.Write((long)4000); writer.Write((uint)1500);
            writer.Write((byte)255);
        });
    }

    private static void Assert(bool value, string detail)
    {
        if (!value) throw new Exception(detail);
    }

    private static void Fails(Action action)
    {
        bool failed = false;
        try { action(); } catch (IOException) { failed = true; } catch (InvalidDataException) { failed = true; } catch (OperationCanceledException) { failed = true; }
        Assert(failed, "Expected a rejected operation");
    }

    private static void Test(string name, Action<string, string, PatchSpec, byte[]> action)
    {
        string original = Path.Combine(folder, "원본 source.iso"), output = Path.Combine(folder, "한글 output.iso");
        File.WriteAllBytes(original, source);
        PatchSpec spec = Spec(); byte[] data = GoodPayload(spec);
        action(original, output, spec, data);
        Assert(File.ReadAllBytes(original).SequenceEqual(source), "Source was changed: " + name);
        Assert(Directory.GetFiles(folder, "*.partial").Length == 0, "Partial file remained: " + name);
        foreach (string file in Directory.GetFiles(folder)) File.Delete(file);
        Console.WriteLine("PASS " + name); count++;
    }

    [STAThread]
    public static int Main()
    {
        System.Windows.Forms.Application.EnableVisualStyles();
        folder = Path.Combine(Path.GetTempPath(), "GunValkyrie-native-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            Test("mixed copy/literal/zero with Korean paths", (a,b,s,p) => {
                ApplyResult result = PatchEngine.Apply(a,b,p,s,CancellationToken.None,quiet);
                Assert(result.verified && File.ReadAllBytes(b).SequenceEqual(Expected()), "Roundtrip mismatch");
            });
            Test("same-size wrong source", (a,b,s,p) => {
                string wrong = Path.Combine(folder,"wrong.iso"); File.WriteAllBytes(wrong,new byte[source.Length]);
                Fails(() => PatchEngine.Apply(wrong,b,p,s,CancellationToken.None,quiet)); Assert(!File.Exists(b),"Unexpected output");
            });
            Test("source is output", (a,b,s,p) => Fails(() => PatchEngine.Apply(a,a,p,s,CancellationToken.None,quiet)));
            Test("existing output preserved", (a,b,s,p) => {
                File.WriteAllText(b,"KEEP"); Fails(() => PatchEngine.Apply(a,b,p,s,CancellationToken.None,quiet));
                Assert(File.ReadAllText(b)=="KEEP","Existing file lost");
            });
            Test("corrupt payload", (a,b,s,p) => {
                p[p.Length/2]^=1; Fails(() => PatchEngine.Apply(a,b,p,s,CancellationToken.None,quiet));
            });
            Test("result hash mismatch", (a,b,s,p) => {
                s.OutputSha256 = new String('0',64); p=GoodPayload(s);
                Fails(() => PatchEngine.Apply(a,b,p,s,CancellationToken.None,quiet)); Assert(!File.Exists(b),"Bad result published");
            });
            Test("cancel during decoding", (a,b,s,p) => {
                using(CancellationTokenSource cancel=new CancellationTokenSource()) {
                    Fails(() => PatchEngine.Apply(a,b,p,s,cancel.Token,(label,value)=> { if(label=="한글 패치 적용 중") cancel.Cancel(); }));
                }
                Assert(!File.Exists(b),"Cancelled result published");
            });
            Test("concurrent output creator preserved", (a,b,s,p) => {
                Fails(() => PatchEngine.Apply(a,b,p,s,CancellationToken.None,(label,value) => {
                    if(label=="결과 ISO 검증 중"&&!File.Exists(b)) File.WriteAllText(b,"OTHER");
                }));
                Assert(File.ReadAllText(b)=="OTHER","Concurrent output overwritten");
            });
            Test("copy source bounds", (a,b,s,p) => {
                p=Package(s,w=>{w.Write((byte)0);w.Write(s.SourceBytes);w.Write((uint)100);w.Write((byte)255);});
                Fails(() => PatchEngine.Apply(a,b,p,s,CancellationToken.None,quiet)); Assert(!File.Exists(b),"Out-of-bounds output");
            });
            Test("output bounds", (a,b,s,p) => {
                p=Package(s,w=>{w.Write((byte)2);w.Write((uint)(s.OutputBytes+1));w.Write((byte)255);});
                Fails(() => PatchEngine.Apply(a,b,p,s,CancellationToken.None,quiet));
            });
            Test("unknown opcode", (a,b,s,p) => {
                p=Package(s,w=>w.Write((byte)77)); Fails(() => PatchEngine.Apply(a,b,p,s,CancellationToken.None,quiet));
            });
            Test("missing end command", (a,b,s,p) => {
                p=Package(s,w=>{}); Fails(() => PatchEngine.Apply(a,b,p,s,CancellationToken.None,quiet));
            });
            Test("source write lock", (a,b,s,p) => {
                bool locked=false;
                PatchEngine.Apply(a,b,p,s,CancellationToken.None,(label,value)=> {
                    if(!locked&&label=="원본 ISO 검증 중") {
                        try {using(FileStream writer=new FileStream(a,FileMode.Open,FileAccess.Write,FileShare.ReadWrite)) {} }
                        catch(IOException) {locked=true;}
                    }
                });
                Assert(locked,"Source allowed concurrent writes");
            });
            Test("header target mismatch", (a,b,s,p) => {
                p[0]^=1; s.PayloadSha256=PatchEngine.Hash(p);
                Fails(() => PatchEngine.Apply(a,b,p,s,CancellationToken.None,quiet));
            });
            Test("trailing decoded instructions", (a,b,s,p) => {
                p=Package(s,w=>{w.Write((byte)2);w.Write((uint)s.OutputBytes);w.Write((byte)255);w.Write((byte)0);});
                Fails(() => PatchEngine.Apply(a,b,p,s,CancellationToken.None,quiet));
            });
            Console.WriteLine("Passed " + count + " tests");
            using(PatcherForm form=new PatcherForm()) {
                form.ShowInTaskbar=false; form.Show(); System.Windows.Forms.Application.DoEvents(); form.PerformLayout();
                var root=form.Controls[0]; root.PerformLayout();
                Console.WriteLine("Form client height="+form.ClientSize.Height+", preferred content height="+root.GetPreferredSize(root.Size).Height);
                Assert(root.GetPreferredSize(root.Size).Height<=form.ClientSize.Height,"GUI content clipped vertically");
                using(var bmp=new System.Drawing.Bitmap(form.Width,form.Height)) { form.DrawToBitmap(bmp,new System.Drawing.Rectangle(0,0,form.Width,form.Height)); bmp.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"patcher-preview.png"),System.Drawing.Imaging.ImageFormat.Png); }
            }
            return 0;
        }
        catch(Exception error)
        {
            Console.WriteLine("FAILED " + error.GetType().FullName + ": " + error.Message);
            return 1;
        }
        finally
        {
            foreach(string file in Directory.GetFiles(folder)) File.Delete(file);
            Directory.Delete(folder); // Non-recursive, exclusively created test folder only.
        }
    }
}
