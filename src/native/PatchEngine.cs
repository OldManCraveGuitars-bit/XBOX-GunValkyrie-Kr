using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Threading;

namespace GunValkyrieKorean
{
    public sealed class PatchSpec
    {
        public long SourceBytes, OutputBytes;
        public string SourceSha256, OutputSha256, PayloadSha256;
    }

    public sealed class ApplyResult
    {
        public bool verified = true;
        public string patcher_version = "0.9.0";
        public string output;
        public long bytes;
        public string sha256;
        public string source_sha256;
    }

    public static class PatchEngine
    {
        public static string Hex(byte[] bytes)
        {
            return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
        }

        public static string Hash(byte[] data)
        {
            using (SHA256 hash = SHA256.Create()) return Hex(hash.ComputeHash(data));
        }

        public static string HashStream(Stream stream, CancellationToken cancel, Action<int> progress)
        {
            stream.Position = 0;
            byte[] buffer = new byte[4 * 1024 * 1024];
            using (SHA256 hash = SHA256.Create())
            {
                int read;
                while ((read = stream.Read(buffer, 0, buffer.Length)) != 0)
                {
                    cancel.ThrowIfCancellationRequested();
                    hash.TransformBlock(buffer, 0, read, buffer, 0);
                    if (progress != null) progress((int)(stream.Position * 100 / Math.Max(1, stream.Length)));
                }
                hash.TransformFinalBlock(new byte[0], 0, 0);
                cancel.ThrowIfCancellationRequested();
                return Hex(hash.Hash);
            }
        }

        public static void ValidatePayload(byte[] payload, PatchSpec spec)
        {
            if (Hash(payload) != spec.PayloadSha256) throw new InvalidDataException("내장 패치 SHA-256이 일치하지 않습니다.");
            using (MemoryStream compressed = new MemoryStream(payload, false))
            using (GZipStream gzip = new GZipStream(compressed, CompressionMode.Decompress))
            using (BinaryReader reader = new BinaryReader(gzip)) CheckHeader(reader, spec);
        }

        private static void CheckHeader(BinaryReader reader, PatchSpec spec)
        {
            if (System.Text.Encoding.ASCII.GetString(reader.ReadBytes(8)) != "GVKR0901" ||
                reader.ReadInt64() != spec.SourceBytes || reader.ReadInt64() != spec.OutputBytes ||
                Hex(reader.ReadBytes(32)) != spec.SourceSha256 || Hex(reader.ReadBytes(32)) != spec.OutputSha256)
                throw new InvalidDataException("패치의 대상 ISO 정보가 일치하지 않습니다.");
        }

        // The decoder accepts three data operations only. It cannot run programs,
        // load modules, access the network, or name any additional output files.
        public static void Decode(Stream source, Stream output, Stream compressed, PatchSpec spec,
                                  CancellationToken cancel, Action<int> progress)
        {
            using (GZipStream gzip = new GZipStream(compressed, CompressionMode.Decompress, true))
            using (BinaryReader reader = new BinaryReader(gzip))
            {
                CheckHeader(reader, spec);
                byte[] buffer = new byte[1024 * 1024];
                int operations = 0;
                while (true)
                {
                    cancel.ThrowIfCancellationRequested();
                    byte opcode = reader.ReadByte();
                    if (opcode == 255) break;
                    if (++operations > 10000000) throw new InvalidDataException("패치 명령 수 초과");
                    long offset = 0;
                    if (opcode == 0) offset = reader.ReadInt64();
                    else if (opcode != 1 && opcode != 2) throw new InvalidDataException("지원하지 않는 패치 명령");
                    long length = reader.ReadUInt32();
                    if (length <= 0 || length > spec.OutputBytes - output.Position)
                        throw new InvalidDataException("출력 ISO 범위를 벗어난 패치 데이터");
                    if (opcode == 0)
                    {
                        if (offset < 0 || offset > spec.SourceBytes - length)
                            throw new InvalidDataException("원본 ISO 범위를 벗어난 참조");
                        source.Position = offset;
                    }
                    if (opcode == 2)
                    {
                        output.SetLength(output.Position + length);
                        output.Position = output.Length;
                    }
                    else
                    {
                        while (length > 0)
                        {
                            cancel.ThrowIfCancellationRequested();
                            int wanted = (int)Math.Min(length, buffer.Length);
                            int read = opcode == 0 ? source.Read(buffer, 0, wanted) : reader.Read(buffer, 0, wanted);
                            if (read <= 0) throw new EndOfStreamException("패치 또는 원본 데이터가 잘렸습니다.");
                            output.Write(buffer, 0, read);
                            length -= read;
                        }
                    }
                    if (progress != null) progress((int)(output.Position * 100 / spec.OutputBytes));
                }
                if (output.Position != spec.OutputBytes || gzip.ReadByte() != -1)
                    throw new InvalidDataException("패치의 전체 출력 길이가 일치하지 않습니다.");
            }
        }

        public static ApplyResult Apply(string sourcePath, string outputPath, byte[] payload,
                                         PatchSpec spec, CancellationToken cancel, Action<string, int> progress)
        {
            sourcePath = Path.GetFullPath(sourcePath);
            outputPath = Path.GetFullPath(outputPath);
            if (String.Equals(sourcePath, outputPath, StringComparison.OrdinalIgnoreCase))
                throw new IOException("원본과 출력 경로가 같습니다. 새 ISO 이름을 지정하세요.");
            if (File.Exists(outputPath) || Directory.Exists(outputPath))
                throw new IOException("출력 경로가 이미 있습니다. 기존 파일은 덮어쓰지 않습니다.");
            if (!String.Equals(Path.GetExtension(outputPath), ".iso", StringComparison.OrdinalIgnoreCase))
                throw new IOException("출력 확장자는 .iso여야 합니다.");
            if (!Directory.Exists(Path.GetDirectoryName(outputPath)))
                throw new IOException("출력 폴더가 없습니다.");
            ValidatePayload(payload, spec);
            string temp = null;
            try
            {
                // Keep the source open without write/delete sharing for the entire operation.
                using (FileStream source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    if (source.Length != spec.SourceBytes) throw new InvalidDataException("지원하지 않는 원본 크기입니다. Japan (En,Ja) ISO 해시를 확인하세요.");
                    string actual = HashStream(source, cancel, p => progress("원본 ISO 검증 중", p * 25 / 100));
                    if (actual != spec.SourceSha256) throw new InvalidDataException("원본 SHA-256이 일치하지 않습니다.\n선택값: " + actual);
                    DriveInfo drive = new DriveInfo(Path.GetPathRoot(outputPath));
                    if (drive.AvailableFreeSpace < spec.OutputBytes + 32L * 1024 * 1024)
                        throw new IOException("출력 드라이브에 최소 800 MB의 빈 공간이 필요합니다.");
                    cancel.ThrowIfCancellationRequested();
                    temp = outputPath + "." + Guid.NewGuid().ToString("N") + ".partial";
                    using (FileStream output = new FileStream(temp, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
                    using (MemoryStream data = new MemoryStream(payload, false))
                    {
                        Decode(source, output, data, spec, cancel, p => progress("한글 패치 적용 중", 25 + p * 50 / 100));
                        output.Flush(true);
                        actual = HashStream(output, cancel, p => progress("결과 ISO 검증 중", 75 + p * 24 / 100));
                        if (output.Length != spec.OutputBytes || actual != spec.OutputSha256)
                            throw new InvalidDataException("결과 ISO의 SHA-256이 일치하지 않습니다. 완성 파일로 저장하지 않습니다.");
                    }
                }
                cancel.ThrowIfCancellationRequested();
                File.Move(temp, outputPath); // Exclusive, same-volume rename. Never overwrite.
                progress("완료! 원본 ISO를 보존했습니다.", 100);
                return new ApplyResult { output = outputPath, bytes = spec.OutputBytes,
                    sha256 = spec.OutputSha256, source_sha256 = spec.SourceSha256 };
            }
            finally
            {
                if (temp != null && File.Exists(temp)) File.Delete(temp);
            }
        }
    }
}
