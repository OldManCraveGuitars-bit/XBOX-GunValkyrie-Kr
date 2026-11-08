using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

[assembly: AssemblyTitle("GunValkyrie Korean ISO Patcher")]
[assembly: AssemblyDescription("Offline data-only ISO patcher. Korea Localized by GiKakNo")]
[assembly: AssemblyCompany("GiKakNo")]
[assembly: AssemblyProduct("GunValkyrie Korean Patch")]
[assembly: AssemblyVersion("0.9.0.0")]
[assembly: AssemblyFileVersion("0.9.0.0")]

namespace GunValkyrieKorean
{
    public static class Program
    {
        public static readonly PatchSpec Spec = new PatchSpec {
            SourceBytes = 722206720, OutputBytes = 722206720,
            SourceSha256 = "33adbae5902b038ba42b156e7f4c2359008b4c02f34eefb05c56f1c6c126ce40",
            OutputSha256 = "c975cdf2f76514f1bc53657567d682443bc804c92dd202144326e1803519cbd4",
            PayloadSha256 = "39cb14b727f715c8633c912a3373521710aed762e4051a7d701d345fcace4c78"
        };

        public static byte[] Payload()
        {
            using (Stream resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("GV.patch"))
            using (MemoryStream memory = new MemoryStream())
            {
                if (resource == null) throw new InvalidDataException("내장 패치 데이터가 없습니다.");
                resource.CopyTo(memory);
                return memory.ToArray();
            }
        }

        private static bool SamePath(string a, string b)
        {
            return a != null && b != null && String.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
        }

        [STAThread]
        public static int Main(string[] args)
        {
            if (args.Length == 0)
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new PatcherForm());
                return 0;
            }
            string report = null;
            bool writeReport = false;
            object result;
            int exitCode;
            try
            {
                Dictionary<string, string> options = new Dictionary<string, string>(StringComparer.Ordinal);
                bool selfTest = false;
                for (int i = 0; i < args.Length; i++)
                {
                    if (args[i] == "--self-test")
                    {
                        if (selfTest) throw new ArgumentException("중복된 옵션");
                        selfTest = true;
                    }
                    else if (args[i] == "--source" || args[i] == "--output" || args[i] == "--report")
                    {
                        string key = args[i];
                        if (i + 1 >= args.Length || options.ContainsKey(key)) throw new ArgumentException("옵션 값이 없거나 중복되었습니다.");
                        options.Add(key, args[++i]);
                    }
                    else throw new ArgumentException("지원하지 않는 옵션: " + args[i]);
                }
                string source = options.ContainsKey("--source") ? options["--source"] : null;
                string output = options.ContainsKey("--output") ? options["--output"] : null;
                if (options.ContainsKey("--report"))
                {
                    report = Path.GetFullPath(options["--report"]);
                    if (File.Exists(report) || Directory.Exists(report)) throw new IOException("보고서 경로가 이미 있습니다. 새 이름을 지정하세요.");
                    if (SamePath(report, source) || SamePath(report, output)) throw new IOException("보고서와 ISO 경로는 달라야 합니다.");
                    if (!Directory.Exists(Path.GetDirectoryName(report))) throw new IOException("보고서 저장 폴더가 없습니다.");
                    writeReport = true;
                }
                byte[] payload = Payload();
                if (selfTest)
                {
                    if (source != null || output != null) throw new ArgumentException("--self-test는 ISO 적용 옵션과 함께 사용할 수 없습니다.");
                    PatchEngine.ValidatePayload(payload, Spec);
                    result = new { verified = true, patcher_version = "0.9.0", payload_sha256 = PatchEngine.Hash(payload),
                        source_sha256 = Spec.SourceSha256, output_sha256 = Spec.OutputSha256,
                        embedded_resources = Assembly.GetExecutingAssembly().GetManifestResourceNames() };
                }
                else
                {
                    if (source == null || output == null) throw new ArgumentException("--source와 --output을 함께 지정하세요.");
                    result = PatchEngine.Apply(source, output, payload, Spec, CancellationToken.None, (label, value) => { });
                }
                exitCode = 0;
            }
            catch (Exception error)
            {
                result = new { verified = false, error = error.Message, patcher_version = "0.9.0" };
                exitCode = 1;
            }
            string json = new JavaScriptSerializer().Serialize(result);
            if (writeReport)
            {
                try
                {
                    using (FileStream stream = new FileStream(report, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    using (StreamWriter writer = new StreamWriter(stream, new UTF8Encoding(false))) writer.WriteLine(json);
                }
                catch (IOException) { return 1; }
                catch (UnauthorizedAccessException) { return 1; }
            }
            Console.WriteLine(json);
            return exitCode;
        }
    }

    public sealed class PatcherForm : Form
    {
        private readonly TextBox sourceBox = new TextBox(), outputBox = new TextBox();
        private readonly Button sourceButton = new Button(), outputButton = new Button(), startButton = new Button(), cancelButton = new Button();
        private readonly Label status = new Label();
        private readonly ProgressBar progress = new ProgressBar();
        private CancellationTokenSource cancellation;
        private bool running;

        private static Label TextLabel(string text, float size, Color color, bool bold)
        {
            return new Label { Text = text, AutoSize = true, Dock = DockStyle.Fill,
                ForeColor = color, Font = new Font("Malgun Gothic", size, bold ? FontStyle.Bold : FontStyle.Regular),
                Margin = new Padding(0, 0, 0, 8), TextAlign = ContentAlignment.MiddleLeft };
        }

        public PatcherForm()
        {
            Text = "GunValkyrie · 한글 패처 V0.9";
            AutoScaleDimensions = new SizeF(96, 96);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(780, 520);
            MinimumSize = new Size(740, 550);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(27, 27, 27);
            ForeColor = Color.WhiteSmoke;
            Font = new Font("Malgun Gothic", 10);
            TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 2, RowCount = 10 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 116));
            Controls.Add(layout);
            for (int i = 0; i < 10; i++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            AddWide(layout, TextLabel("건 발키리 / 한국어", 23, Color.FromArgb(142, 237, 231), true), 0);
            AddWide(layout, TextLabel("V0.9 · 한글 ISO 패처  |  Korea Localized by GiKakNo", 10, ForeColor, false), 1);
            AddWide(layout, TextLabel("1. Japan (En,Ja) 원본 ISO", 10, ForeColor, false), 2);
            ConfigureRow(layout, sourceBox, sourceButton, "원본 선택", 3);
            AddWide(layout, TextLabel("2. 한글 ISO를 저장할 새 경로", 10, ForeColor, false), 4);
            ConfigureRow(layout, outputBox, outputButton, "저장 경로", 5);
            Label note = TextLabel("원본을 보존하고 패치 적용 전·후 해시를 자동으로 검사합니다.\n일본판 (En,Ja) 원본 ISO와 새 저장 경로를 선택해 주세요.", 9, Color.Silver, false);
            note.Margin = new Padding(0, 8, 0, 18);
            AddWide(layout, note, 6);
            progress.Dock = DockStyle.Fill;
            progress.Height = 22;
            progress.Margin = new Padding(0, 0, 0, 9);
            AddWide(layout, progress, 7);
            status.Text = "원본 선택 → 새 저장 경로 지정 → 한글 패치 시작";
            status.AutoSize = true;
            status.Dock = DockStyle.Fill;
            AddWide(layout, status, 8);
            FlowLayoutPanel actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Margin = new Padding(0, 20, 0, 0) };
            startButton.Text = "한글 패치 시작";
            startButton.AutoSize = true;
            startButton.Padding = new Padding(10, 7, 10, 7);
            cancelButton.Text = "취소";
            cancelButton.AutoSize = true;
            cancelButton.Enabled = false;
            cancelButton.Padding = new Padding(10, 7, 10, 7);
            actions.Controls.Add(startButton);
            actions.Controls.Add(cancelButton);
            foreach (Button button in new[] { sourceButton, outputButton, startButton, cancelButton })
            {
                button.ForeColor = Color.Black;
                button.UseVisualStyleBackColor = false;
                button.BackColor = Color.FromArgb(184, 232, 225);
                button.FlatStyle = FlatStyle.Flat;
                button.FlatAppearance.BorderColor = Color.FromArgb(100, 172, 165);
            }
            AddWide(layout, actions, 9);
            sourceButton.Click += ChooseSource;
            outputButton.Click += ChooseOutput;
            startButton.Click += StartPatch;
            cancelButton.Click += (sender, e) => { if (cancellation != null) cancellation.Cancel(); };
            FormClosing += (sender, e) => {
                if (running) { cancellation.Cancel(); e.Cancel = true; status.Text = "취소 처리 중입니다. 정리가 끝나면 닫아주세요."; }
            };
        }

        private static void AddWide(TableLayoutPanel layout, Control control, int row)
        {
            layout.Controls.Add(control, 0, row);
            layout.SetColumnSpan(control, 2);
        }

        private static void ConfigureRow(TableLayoutPanel layout, TextBox box, Button button, string label, int row)
        {
            box.Dock = DockStyle.Fill;
            box.Margin = new Padding(0, 2, 10, 16);
            button.Dock = DockStyle.Fill;
            button.AutoSize = true;
            button.Text = label;
            button.Margin = new Padding(0, 0, 0, 14);
            layout.Controls.Add(box, 0, row);
            layout.Controls.Add(button, 1, row);
        }

        private void ChooseSource(object sender, EventArgs args)
        {
            using (OpenFileDialog dialog = new OpenFileDialog { Title = "Japan (En,Ja) 원본 ISO", Filter = "ISO 이미지|*.iso|모든 파일|*.*" })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                sourceBox.Text = dialog.FileName;
                if (outputBox.Text.Length == 0) outputBox.Text = Path.Combine(Path.GetDirectoryName(dialog.FileName), "GunValkyrie (Japan) [Korean V0.9].iso");
            }
        }

        private void ChooseOutput(object sender, EventArgs args)
        {
            using (SaveFileDialog dialog = new SaveFileDialog { Title = "새 한글 ISO 경로", Filter = "ISO 이미지|*.iso", FileName = "GunValkyrie (Japan) [Korean V0.9].iso", DefaultExt = "iso" })
            {
                if (dialog.ShowDialog(this) == DialogResult.OK) outputBox.Text = dialog.FileName;
            }
        }

        private void SetRunning(bool value)
        {
            running = value;
            sourceBox.Enabled = outputBox.Enabled = sourceButton.Enabled = outputButton.Enabled = startButton.Enabled = !value;
            cancelButton.Enabled = value;
        }

        private async void StartPatch(object sender, EventArgs args)
        {
            string source = sourceBox.Text.Trim(), output = outputBox.Text.Trim();
            if (source.Length == 0 || output.Length == 0) { MessageBox.Show(this, "원본 ISO와 새 저장 경로를 지정하세요.", "파일 선택"); return; }
            cancellation = new CancellationTokenSource();
            SetRunning(true);
            progress.Value = 0;
            int lastProgress = -1;
            try
            {
                ApplyResult result = await Task.Run(() => PatchEngine.Apply(source, output, Program.Payload(), Program.Spec,
                    cancellation.Token, (label, value) => {
                        if (value == lastProgress) return;
                        lastProgress = value;
                        BeginInvoke(new Action(() => { status.Text = label; progress.Value = Math.Max(0, Math.Min(100, value)); }));
                    }));
                status.Text = "완료! 결과 ISO SHA-256 검증 통과";
                MessageBox.Show(this, "한글 ISO 생성 및 검증을 마쳤습니다.\n\n" + result.output, "패치 완료", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (OperationCanceledException) { status.Text = "취소했습니다. 원본은 보존했고 작업 임시 파일은 정리했습니다."; }
            catch (Exception error) { status.Text = "작업을 완료하지 못했습니다. 원본은 보존했습니다."; MessageBox.Show(this, error.Message, "패치 안내", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            finally { SetRunning(false); cancellation.Dispose(); cancellation = null; }
        }
    }
}
