using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;
using K4os.Compression.LZ4;

namespace VirtualDesktopHelper
{
    /// <summary>
    /// Obfuscated IL slider-cap patch for VirtualDesktop.Mobile.dll inside the Quest APK blob.
    /// Offsets and keystore material are XOR-masked so they are not plaintext in the binary.
    /// </summary>
    static class CapPatch
    {
        const uint Mask = 0xC2ACED01u;
        const int DescEntry = 28;
        const string BlobPath = "lib/arm64-v8a/libassemblies.arm64-v8a.blob.so";

        // 1.34.22.0 Mobile.dll size 544256: 0x13B0C, 0x14E45, 0x2BFE3
        static readonly uint[] Enc22 = { 0x13B0C ^ Mask, 0x14E45 ^ Mask, 0x2BFE3 ^ Mask };
        // 1.34.19.0 size 540672
        static readonly uint[] Enc19 = { 0x1396C ^ Mask, 0x14CA5 ^ Mask, 0x2BC97 ^ Mask };

        static int[] Offs(int size)
        {
            uint[] enc = size == 544256 ? Enc22 : size == 540672 ? Enc19 : null;
            if (enc == null) return null;
            var a = new int[enc.Length];
            for (int i = 0; i < enc.Length; i++) a[i] = (int)(enc[i] ^ Mask);
            return a;
        }

        static string Unmask(byte[] x)
        {
            var b = (byte[])x.Clone();
            for (int i = 0; i < b.Length; i++) b[i] ^= 0x5A;
            return Encoding.ASCII.GetString(b);
        }

        // vdpatch / vdpatch2026 XOR 0x5A
        static readonly byte[] EncAlias = { 0x2C, 0x3E, 0x2A, 0x3B, 0x2E, 0x39, 0x32 };
        static readonly byte[] EncPass = { 0x2C, 0x3E, 0x2A, 0x3B, 0x2E, 0x39, 0x32, 0x68, 0x6A, 0x68, 0x6C };

        public static string Apply(string apkPath, int cap, string outPath, StringBuilder log)
        {
            if (cap < 50 || cap > 4000) throw new ArgumentOutOfRangeException("cap");
            byte[] blob;
            using (var z = ZipFile.OpenRead(apkPath))
            {
                var e = z.GetEntry(BlobPath);
                if (e == null) throw new InvalidOperationException("blob missing");
                using (var s = e.Open())
                using (var ms = new MemoryStream())
                {
                    s.CopyTo(ms);
                    blob = ms.ToArray();
                }
            }
            var patched = PatchBlob(blob, cap, log);
            var unsigned = outPath + ".unsigned";
            WriteApk(apkPath, unsigned, patched);
            var aligned = outPath + ".aligned";
            var zipalign = FindTool("zipalign.exe");
            var apksigner = FindTool("apksigner.bat");
            var ks = FindKeystore();
            if (zipalign == null || apksigner == null || ks == null)
            {
                File.Copy(unsigned, outPath, true);
                try { File.Delete(unsigned); } catch { }
                log.AppendLine(L.T(
                    "Wrote unsigned APK (no zipalign/apksigner/keystore). Sign it before install.",
                    "已写出未签名 APK（没找到 zipalign/apksigner/keystore）。安装前要签名。"));
                return outPath;
            }
            Run(zipalign, "-f -p 4 \"" + unsigned + "\" \"" + aligned + "\"", log);
            try { File.Delete(unsigned); } catch { }
            if (File.Exists(outPath)) File.Delete(outPath);
            var pass = Unmask(EncPass);
            var alias = Unmask(EncAlias);
            Run(apksigner,
                "sign --ks \"" + ks + "\" --ks-pass pass:" + pass + " --key-pass pass:" + pass
                + " --ks-key-alias " + alias
                + " --v1-signing-enabled false --v2-signing-enabled true --v3-signing-enabled true --out \""
                + outPath + "\" \"" + aligned + "\"", log);
            try { File.Delete(aligned); } catch { }
            return outPath;
        }

        static byte[] PatchBlob(byte[] blob, int cap, StringBuilder log)
        {
            int payload = IndexOf(blob, Encoding.ASCII.GetBytes("XABA"), 0);
            if (payload < 0) throw new InvalidOperationException("XABA");
            int entryCount = BitConverter.ToInt32(blob, payload + 8);
            int indexSize = BitConverter.ToInt32(blob, payload + 16);
            int desc = payload + 20 + indexSize;
            var xalz = new List<int>();
            int p = payload;
            while (true)
            {
                int pos = IndexOf(blob, Encoding.ASCII.GetBytes("XALZ"), p);
                if (pos < 0) break;
                xalz.Add(pos);
                p = pos + 4;
            }
            if (xalz.Count != entryCount) throw new InvalidOperationException("XALZ count");
            var dataSize = new int[entryCount];
            var uncomp = new int[entryCount];
            var idx = new int[entryCount];
            for (int i = 0; i < entryCount; i++)
            {
                dataSize[i] = BitConverter.ToInt32(blob, desc + i * DescEntry + 8);
                idx[i] = BitConverter.ToInt32(blob, xalz[i] + 4);
                uncomp[i] = BitConverter.ToInt32(blob, xalz[i] + 8);
            }
            int target = -1;
            int[] offs = null;
            for (int i = 0; i < entryCount; i++)
            {
                offs = Offs(uncomp[i]);
                if (offs != null) { target = i; break; }
            }
            if (target < 0) throw new InvalidOperationException("Mobile.dll not in blob");
            int start = xalz[target] + 12;
            int end = target + 1 < entryCount ? xalz[target + 1] : xalz[target] + dataSize[target];
            var comp = new byte[end - start];
            Buffer.BlockCopy(blob, start, comp, 0, comp.Length);
            var raw = new byte[uncomp[target]];
            int decoded = LZ4Codec.Decode(comp, 0, comp.Length, raw, 0, raw.Length);
            if (decoded != raw.Length) throw new InvalidOperationException("lz4 decode");
            var nb = BitConverter.GetBytes(cap);
            foreach (var o in offs)
            {
                int old = BitConverter.ToInt32(raw, o);
                log.AppendLine("IL 0x" + o.ToString("X") + " " + old + " -> " + cap);
                Buffer.BlockCopy(nb, 0, raw, o, 4);
            }
            int max = LZ4Codec.MaximumOutputSize(raw.Length);
            var ncomp = new byte[max];
            int nlen = LZ4Codec.Encode(raw, 0, raw.Length, ncomp, 0, ncomp.Length, LZ4Level.L12_MAX);
            var rec = new byte[12 + nlen];
            Encoding.ASCII.GetBytes("XALZ").CopyTo(rec, 0);
            BitConverter.GetBytes(idx[target]).CopyTo(rec, 4);
            BitConverter.GetBytes(raw.Length).CopyTo(rec, 8);
            Buffer.BlockCopy(ncomp, 0, rec, 12, nlen);

            int first = xalz[0];
            int lastEnd = xalz[entryCount - 1] + dataSize[entryCount - 1];
            var header = new byte[first];
            Buffer.BlockCopy(blob, 0, header, 0, first);
            var payloadBytes = new List<byte>(lastEnd - first + 4096);
            for (int i = 0; i < entryCount; i++)
            {
                byte[] piece;
                if (i == target) piece = rec;
                else
                {
                    piece = new byte[dataSize[i]];
                    Buffer.BlockCopy(blob, xalz[i], piece, 0, dataSize[i]);
                }
                int newOff = payloadBytes.Count + (first - payload);
                BitConverter.GetBytes(newOff).CopyTo(header, desc + i * DescEntry + 4);
                BitConverter.GetBytes(piece.Length).CopyTo(header, desc + i * DescEntry + 8);
                payloadBytes.AddRange(piece);
            }
            var tail = new byte[blob.Length - lastEnd];
            Buffer.BlockCopy(blob, lastEnd, tail, 0, tail.Length);
            var body = payloadBytes.ToArray();
            var output = new byte[header.Length + body.Length + tail.Length];
            Buffer.BlockCopy(header, 0, output, 0, header.Length);
            Buffer.BlockCopy(body, 0, output, header.Length, body.Length);
            Buffer.BlockCopy(tail, 0, output, header.Length + body.Length, tail.Length);
            int growth = (header.Length + body.Length) - lastEnd;
            if (growth != 0 && output[0] == 0x7F && output[1] == (byte)'E')
            {
                long oldShoff = BitConverter.ToInt64(blob, 40);
                long newShoff = oldShoff + growth;
                BitConverter.GetBytes(newShoff).CopyTo(output, 40);
                int shentsize = BitConverter.ToInt16(blob, 58);
                int shnum = BitConverter.ToInt16(blob, 60);
                for (int si = 0; si < shnum; si++)
                {
                    int sh = (int)newShoff + si * shentsize;
                    long shOffset = BitConverter.ToInt64(output, sh + 24);
                    long shSize = BitConverter.ToInt64(output, sh + 32);
                    if (shOffset <= payload && payload < shOffset + shSize)
                    {
                        BitConverter.GetBytes(shSize + growth).CopyTo(output, sh + 32);
                        break;
                    }
                }
            }
            return output;
        }

        static void WriteApk(string src, string dest, byte[] blob)
        {
            if (File.Exists(dest)) File.Delete(dest);
            using (var zin = ZipFile.OpenRead(src))
            using (var zout = ZipFile.Open(dest, ZipArchiveMode.Create))
            {
                foreach (var e in zin.Entries)
                {
                    var name = e.FullName.Replace('\\', '/');
                    var upper = name.ToUpperInvariant();
                    if (name.StartsWith("META-INF/", StringComparison.OrdinalIgnoreCase)
                        && (upper.EndsWith(".SF") || upper.EndsWith(".RSA") || upper.EndsWith(".DSA") || upper.EndsWith(".MF")))
                        continue;
                    var ne = zout.CreateEntry(e.FullName, name == BlobPath ? CompressionLevel.NoCompression : CompressionLevel.Optimal);
                    using (var os = ne.Open())
                    {
                        if (name == BlobPath) os.Write(blob, 0, blob.Length);
                        else using (var ins = e.Open()) ins.CopyTo(os);
                    }
                }
            }
        }

        static int IndexOf(byte[] hay, byte[] needle, int start)
        {
            for (int i = start; i <= hay.Length - needle.Length; i++)
            {
                bool ok = true;
                for (int j = 0; j < needle.Length; j++)
                    if (hay[i + j] != needle[j]) { ok = false; break; }
                if (ok) return i;
            }
            return -1;
        }

        static string FindTool(string name)
        {
            var cands = new List<string>();
            var sdk = Environment.GetEnvironmentVariable("ANDROID_HOME") ?? Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT");
            if (!string.IsNullOrEmpty(sdk))
                cands.Add(Path.Combine(sdk, "build-tools", "35.0.0", name));
            cands.Add(@"D:\Software\Android\Sdk\build-tools\35.0.0\" + name);
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            cands.Add(Path.Combine(local, @"Android\Sdk\build-tools\35.0.0\" + name));
            foreach (var c in cands) if (File.Exists(c)) return c;
            return null;
        }

        static string FindKeystore()
        {
            var cands = new[]
            {
                @"D:\Project\VirtualDesktop\analysis\apk_patch\vd-patch-release.keystore",
                Path.Combine(Path.GetDirectoryName(System.Windows.Forms.Application.ExecutablePath) ?? ".", "vd-patch-release.keystore"),
                Path.Combine(Paths.AppDir, "vd-patch-release.keystore"),
            };
            foreach (var c in cands) if (File.Exists(c)) return c;
            return null;
        }

        static void Run(string file, string args, StringBuilder log)
        {
            var psi = new ProcessStartInfo(file, args)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using (var p = Process.Start(psi))
            {
                var o = p.StandardOutput.ReadToEnd();
                var e = p.StandardError.ReadToEnd();
                p.WaitForExit();
                if (!string.IsNullOrEmpty(o)) log.AppendLine(o.Trim());
                if (!string.IsNullOrEmpty(e)) log.AppendLine(e.Trim());
                if (p.ExitCode != 0) throw new InvalidOperationException(file + " exit " + p.ExitCode);
            }
        }
    }
}
