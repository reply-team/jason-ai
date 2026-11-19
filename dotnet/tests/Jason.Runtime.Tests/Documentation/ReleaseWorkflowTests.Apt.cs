using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Jason.Contracts.Update;
using YamlDotNet.RepresentationModel;
using static Jason.Runtime.Tests.Documentation.Workflows;

namespace Jason.Runtime.Tests.Documentation;

/// <summary>
/// The apt repository a release carries: the index files beside the deb, written and signed by
/// <c>.github/scripts/write-apt-index.sh</c> in a job of the release's own that holds the signing subkey and may write
/// nothing, and read back by <c>.github/scripts/check-apt-index.sh</c> as a machine's apt reads them, before anything is
/// published. Both scripts run here on a small deb this test builds and on keys it makes, through the real dpkg-deb,
/// gpg, gpgv and apt-get; nothing is stood in for.
/// </summary>
public partial class ReleaseWorkflowTests
{
    private static readonly string[] AptWriter = [".github", "scripts", "write-apt-index.sh"];
    private static readonly string[] AptChecker = [".github", "scripts", "check-apt-index.sh"];
    private static readonly string[] AptKeyCheck = [".github", "scripts", "check-apt-key.sh"];
    private static readonly string[] AptKeyring = ["install", "jason-desktop-archive-keyring.gpg"];
    private const string AptJob = "apt-index";
    private const string AptSecret = "APT_SIGNING_KEY";
    private const string AptArtifact = "jason-apt-index";
    private const string AptWriteStep = "Write the apt repository's index, signed with the release's key";
    private const string AptCheckStep = "Read the index back as a machine's apt does";
    private const string AptCiStep = "Write and read back the apt repository's index as a release does, signed with a key this run makes";
    private const string AptRouteStep = "The deb's apt route, from the install through the repository to the purge";
    private const string Deb = "jason-desktop-linux-x64.deb";

    /// <summary>
    /// The release publishes the index only as its own job wrote and checked it: the release job needs that job, so a
    /// release whose deb has no index, or whose index a machine's apt would refuse, publishes nothing, as a missing
    /// platform publishes nothing. The job holds the signing subkey and nothing that writes: the signing environment,
    /// read access alone, the deb the Linux leg built, the writer with the secret, the repository's key held to its expiry
    /// in the same step right before it signs, the check with the repository's own public key after it, and an upload of
    /// the four files under a name the release's download collects. The manifest's line lists them in checksums.txt, and
    /// the release attaches them.
    /// </summary>
    [Fact]
    [Critical(Family.I1, "a release that publishes a deb without its checked index makes every Ubuntu machine's apt update fail")]
    public void The_release_publishes_the_apt_index_only_as_its_own_job_wrote_and_checked_it()
    {
        var jobs = Mapping(Parse(Read(Release)), "jobs");
        Assert.Contains(AptJob, Assert.IsType<YamlSequenceNode>(Node(PublishingJob(), "needs")).Children.Select(need => need.ToString()));

        var job = Mapping(jobs, AptJob);
        Assert.Equal(["build"], Assert.IsType<YamlSequenceNode>(Node(job, "needs")).Children.Select(need => need.ToString()));
        Assert.Equal("ubuntu-latest", Scalar(job, "runs-on"));
        Assert.Equal("signing", Scalar(job, "environment"));
        Assert.Equal(["contents=read"], Mapping(job, "permissions").Children.Select(pair => $"{pair.Key}={pair.Value}"));
        Assert.Null(Node(job, "if"));
        Assert.Null(Node(job, "continue-on-error"));
        Assert.Null(Node(job, "env"));

        var steps = JobSteps(job).ToList();
        Assert.All(steps, step => Assert.Null(Node(step, "if")));
        Assert.All(steps, step => Assert.Null(Node(step, "continue-on-error")));
        Assert.Equal(
            ["actions/checkout@v7", "actions/download-artifact@v7", AptWriteStep, AptCheckStep, "actions/upload-artifact@v7"],
            steps.Select(step => Scalar(step, "name") ?? Scalar(step, "uses")));

        var download = Mapping(steps[1], "with");
        Assert.Equal(["name=jason-desktop-linux-x64", "path=artifacts/apt"], download.Children.Select(pair => $"{pair.Key}={pair.Value}"));

        Assert.Equal(
            [$"bash .github/scripts/check-apt-key.sh {string.Join('/', AptKeyring)}", "bash .github/scripts/write-apt-index.sh artifacts/apt"],
            (Scalar(steps[2], "run") ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries));
        Assert.Equal([$"{AptSecret}=${{{{ secrets.{AptSecret} }}}}"], Mapping(steps[2], "env").Children.Select(pair => $"{pair.Key}={pair.Value}"));
        Assert.Equal($"bash .github/scripts/check-apt-index.sh artifacts/apt {string.Join('/', AptKeyring)}", Scalar(steps[3], "run"));
        Assert.Null(Node(steps[3], "env"));

        var upload = Mapping(steps[4], "with");
        Assert.Equal(AptArtifact, Scalar(upload, "name"));
        Assert.Equal(ReleaseAssets.AptIndex.Select(file => $"artifacts/apt/{file}"), (Scalar(upload, "path") ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries));
        Assert.Equal("error", Scalar(upload, "if-no-files-found"));
        Assert.StartsWith("jason-", AptArtifact, StringComparison.Ordinal);
        Assert.Equal("jason-*", Scalar(Mapping(Assert.Single(JobSteps(PublishingJob()), step => Scalar(step, "uses") == "actions/download-artifact@v7"), "with"), "pattern"));

        Assert.Equal(["Packages", "Release", "InRelease", "Release.gpg"], ReleaseAssets.AptIndex);
        var manifest = Assert.Single(Read(Release).Split('\n'), line => line.Contains("./.github/scripts/Write-Manifest.ps1", StringComparison.Ordinal));
        Assert.Contains($",{Deb},jason-desktop-linux-x64.AppImage,latest-linux.yml,{string.Join(',', ReleaseAssets.AptIndex)} ", manifest, StringComparison.Ordinal);
    }

    /// <summary>
    /// The apt repository's signing subkey stands in one place, the writer step's environment, in the job that signs the
    /// index, which may not write, runs no test and packages nothing: the key every machine with the repository trusts
    /// never shares a runner with the publishing token or with test code. Held against release.yml as it is and against
    /// copies of it that move it: into the reading step, under a second name in the writer, into the release job, into a
    /// test in its own job, the job made to write, the whole context in the job, and the writer left without it. Each copy
    /// is refused, and release.yml itself is not.
    /// </summary>
    [Theory]
    [InlineData("as it is", false)]
    [InlineData("in the reading step", true)]
    [InlineData("a second name in the writer", true)]
    [InlineData("in the release job", true)]
    [InlineData("a test in its job", true)]
    [InlineData("its job made to write", true)]
    [InlineData("the whole context in its job", true)]
    [InlineData("gone from the writer", true)]
    public void Only_the_apt_index_s_writer_holds_its_signing_key(string copy, bool refused)
    {
        var text = Read(Release).ReplaceLineEndings("\n");
        const string Secret = "${{ secrets." + AptSecret + " }}";
        const string Writer = "      - name: " + AptWriteStep + "\n        env:\n          " + AptSecret + ": " + Secret + "\n";
        const string Reader = "      - name: " + AptCheckStep + "\n";
        const string Header = "    name: The apt repository's index\n    needs: [build]\n    runs-on: ubuntu-latest\n    environment: signing\n    permissions:\n      contents: read\n";
        const string Publishing = "    concurrency:\n      group: release-publish\n";

        string Moved(string from, string to)
        {
            Assert.Equal(1, System.Text.RegularExpressions.Regex.Count(text, System.Text.RegularExpressions.Regex.Escape(from)));
            return text.Replace(from, to, StringComparison.Ordinal);
        }

        var moved = copy switch
        {
            "as it is" => text,
            "in the reading step" => Moved(Reader, Reader + "        env:\n          " + AptSecret + ": " + Secret + "\n"),
            "a second name in the writer" => Moved(Writer, Writer + "          OTHER: " + Secret + "\n"),
            "in the release job" => Moved(Publishing, "    env:\n      KEY: " + Secret + "\n" + Publishing),
            "a test in its job" => Moved(Writer, "      - run: dotnet test --solution dotnet/Jason.slnx\n\n" + Writer),
            "its job made to write" => Moved(Header, Header.Replace("contents: read", "contents: write", StringComparison.Ordinal)),
            "the whole context in its job" => Moved(Header, Header + "    env:\n      ALL: ${{ toJSON(secrets) }}\n"),
            "gone from the writer" => Moved(Writer, "      - name: " + AptWriteStep + "\n        env:\n          " + AptSecret + ": ''\n"),
            _ => throw new ArgumentOutOfRangeException(nameof(copy)),
        };

        var problems = SecretsOutOfPlace(moved);
        Assert.True(refused == (problems.Count > 0), $"{copy}: {string.Join("; ", problems)}");
    }

    /// <summary>
    /// Every pull request's desktop job writes the index from the deb it built and reads it back with the release's own
    /// two lines, the writer and then the check, in one step right after the package call: there is no signing secret
    /// there, so the step makes a key of the run's own, shaped as the release's (an Ed25519 key that certifies, with a
    /// signing subkey, three years), hands the writer that subkey alone, checks against that key's public half, and says
    /// in a notice that this index is signed with a key the run made. It never holds the repository's key to its expiry:
    /// that check stops what the release signs, and no pull request.
    /// </summary>
    [Fact]
    public void Ci_s_desktop_job_writes_and_reads_back_the_index_as_a_release_does()
    {
        var steps = JobSteps(Mapping(Mapping(Parse(Read(Ci)), "jobs"), "desktop")).ToList();
        var package = steps.FindIndex(step => Scalar(step, "uses") == "./.github/actions/package");
        Assert.True(package >= 0);
        var step = steps[package + 1];
        Assert.Equal(AptCiStep, Scalar(step, "name"));
        Assert.Equal("bash", Scalar(step, "shell"));
        Assert.Equal(".", Scalar(step, "working-directory"));
        Assert.Null(Node(step, "if"));
        Assert.Null(Node(step, "continue-on-error"));

        var run = Scalar(step, "run") ?? "";
        var lines = run.Split('\n').Select(line => line.Trim()).ToList();
        Assert.DoesNotContain("secrets", run, StringComparison.Ordinal);
        Assert.DoesNotContain(lines, line => line.Contains("check-apt-key.sh", StringComparison.Ordinal));
        Assert.Contains("gpg --batch --passphrase '' --quick-generate-key 'Jason apt index, this run alone' ed25519 cert 3y", lines);
        Assert.Contains("gpg --batch --passphrase '' --quick-add-key \"$fpr\" ed25519 sign 3y", lines);
        Assert.Contains("APT_SIGNING_KEY=\"$(gpg --armor --export-secret-subkeys \"$fpr\")\" bash .github/scripts/write-apt-index.sh desktop/dist", lines);
        Assert.Contains("bash .github/scripts/check-apt-index.sh desktop/dist \"$keyring\"", lines);
        Assert.True(
            lines.FindIndex(line => line.Contains("write-apt-index.sh", StringComparison.Ordinal)) < lines.FindIndex(line => line.Contains("check-apt-index.sh", StringComparison.Ordinal)),
            "The check reads what the writer wrote, so it runs after it.");
        Assert.Contains(lines, line => line.StartsWith("echo \"::notice title=apt index::", StringComparison.Ordinal) && line.Contains("a key this run made", StringComparison.Ordinal));
    }

    /// <summary>
    /// The public key the release's check reads, and the one a deb will carry, is the repository's and public alone. A
    /// key the tree holds is read by everyone, so a secret key packet in it is the signing key published: read packet by
    /// packet, with no tool, it holds a public key, its user id, its signatures and a public subkey, never a secret key or
    /// subkey. And it is the key whose signing subkey the signing environment holds, by fingerprint, the primary and the
    /// subkey: another key in the file is a decision, never a drift, since every machine trusts the key its deb carried.
    /// </summary>
    [Fact]
    public void The_repository_s_apt_key_is_its_own_and_public_alone()
    {
        var packets = Packets(File.ReadAllBytes(Path.Combine([RepositoryRoot(), .. AptKeyring])));
        Assert.All(packets, packet => Assert.Contains(packet.Tag, new[] { 2, 6, 13, 14 }));
        Assert.Equal(["A3574B95976626A07094C2DE16D948734DD07510"], packets.Where(packet => packet.Tag == 6).Select(Fingerprint));
        Assert.Equal(["46C07C416710A94A1ECA00DEDDF1CB985DC1F50E"], packets.Where(packet => packet.Tag == 14).Select(Fingerprint));
    }

    /// <summary>An OpenPGP version 4 key's fingerprint: SHA-1 over 0x99, the key packet's length in two bytes, and its body.</summary>
    private static string Fingerprint((int Tag, byte[] Body) packet) =>
        Convert.ToHexString(SHA1.HashData([0x99, (byte)(packet.Body.Length >> 8), (byte)packet.Body.Length, .. packet.Body]));

    /// <summary>Each OpenPGP packet in a binary key file, its tag and its body, in both the old and the new header formats.</summary>
    private static List<(int Tag, byte[] Body)> Packets(byte[] bytes)
    {
        var packets = new List<(int Tag, byte[] Body)>();
        var at = 0;
        while (at < bytes.Length)
        {
            var header = bytes[at++];
            Assert.True((header & 0x80) != 0, $"The byte at {at - 1} starts no OpenPGP packet.");
            int tag;
            long length;
            if ((header & 0x40) != 0)
            {
                tag = header & 0x3F;
                var first = bytes[at++];
                (length, at) = first switch
                {
                    < 192 => (first, at),
                    < 224 => (((first - 192) << 8) + bytes[at] + 192, at + 1),
                    255 => ((long)bytes[at] << 24 | (long)bytes[at + 1] << 16 | (long)bytes[at + 2] << 8 | bytes[at + 3], at + 4),
                    _ => throw new InvalidDataException("A key file holds no partial-length packet."),
                };
            }
            else
            {
                tag = (header >> 2) & 0x0F;
                (length, at) = (header & 0x03) switch
                {
                    0 => (bytes[at], at + 1),
                    1 => (bytes[at] << 8 | bytes[at + 1], at + 2),
                    2 => ((long)bytes[at] << 24 | (long)bytes[at + 1] << 16 | (long)bytes[at + 2] << 8 | bytes[at + 3], at + 4),
                    _ => throw new InvalidDataException("A key file holds no packet of indeterminate length."),
                };
            }

            packets.Add((tag, bytes[at..(at + (int)length)]));
            at += (int)length;
        }

        Assert.Equal(bytes.Length, at);
        return packets;
    }

    /// <summary>
    /// The writer, given a deb and the secret subkey, writes the four files and nothing else: Packages, the deb's own
    /// control fields with its file name, size, MD5 and SHA-256; Release, Jason's origin, label and architecture, the
    /// suite stable, the date it was written, and Packages' MD5 and SHA-256 with its size; InRelease, Release clearsigned;
    /// Release.gpg,
    /// Release's detached signature. gpgv takes both with the key's public half, made by its signing subkey, and refuses
    /// both with another key; the signing home it made is gone afterwards.
    /// </summary>
    [Fact]
    public void The_writer_describes_the_deb_and_signs_with_the_subkey()
    {
        RequireAptTools();
        using var tree = new TempTree();
        var (deb, control) = SmallDeb(tree, "1.2.3~rc.1");
        var directory = Path.GetDirectoryName(deb)!;
        var key = ThrowawayKey(tree, "3y", "3y");
        var other = ThrowawayKey(tree, "3y", "3y");
        var before = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        var (exit, said) = WriteAptIndex(tree, directory, key.Secret);
        Assert.True(exit == 0, said);
        Assert.Equal(["InRelease", "Packages", "Release", "Release.gpg", Deb], Directory.EnumerateFiles(directory).Select(Path.GetFileName).Order(StringComparer.Ordinal));

        var bytes = File.ReadAllBytes(deb);
        Assert.Equal(
            control
            + $"Filename: ./{Deb}\n"
            + $"Size: {bytes.Length.ToString(CultureInfo.InvariantCulture)}\n"
            + $"MD5sum: {Convert.ToHexStringLower(MD5.HashData(bytes))}\n"
            + $"SHA256: {Convert.ToHexStringLower(SHA256.HashData(bytes))}\n",
            File.ReadAllText(Path.Combine(directory, "Packages")));

        var packages = File.ReadAllBytes(Path.Combine(directory, "Packages"));
        var release = File.ReadAllText(Path.Combine(directory, "Release")).Split('\n');
        Assert.Equal(["Origin: Jason", "Label: Jason", "Suite: stable", "Architectures: amd64"], release[..4]);
        Assert.StartsWith("Date: ", release[4], StringComparison.Ordinal);
        var date = DateTimeOffset.ParseExact(release[4]["Date: ".Length..], "ddd, dd MMM yyyy HH:mm:ss 'UTC'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
        Assert.InRange(date, before, DateTimeOffset.UtcNow);
        var size = packages.Length.ToString(CultureInfo.InvariantCulture);
        Assert.Equal(
            ["MD5Sum:", $" {Convert.ToHexStringLower(MD5.HashData(packages))} {size} Packages", "SHA256:", $" {Convert.ToHexStringLower(SHA256.HashData(packages))} {size} Packages", ""],
            release[5..]);

        var signed = Path.Combine(tree.Root, "signed");
        var inRelease = Gpgv(tree, key.Public, "--output", signed, Path.Combine(directory, "InRelease"));
        Assert.True(inRelease.Exit == 0, inRelease.Said);
        Assert.Contains($"[GNUPG:] GOODSIG {key.Subkey[^16..]} ", inRelease.Said, StringComparison.Ordinal);
        Assert.Contains($"[GNUPG:] VALIDSIG {key.Subkey} ", inRelease.Said, StringComparison.Ordinal);
        Assert.EndsWith($" {key.Primary}", inRelease.Said.Split('\n').Single(line => line.StartsWith("[GNUPG:] VALIDSIG ", StringComparison.Ordinal)), StringComparison.Ordinal);
        Assert.Equal(File.ReadAllBytes(Path.Combine(directory, "Release")), File.ReadAllBytes(signed));

        var detached = Gpgv(tree, key.Public, Path.Combine(directory, "Release.gpg"), Path.Combine(directory, "Release"));
        Assert.True(detached.Exit == 0, detached.Said);
        Assert.Contains($"[GNUPG:] VALIDSIG {key.Subkey} ", detached.Said, StringComparison.Ordinal);

        foreach (var file in (string[][])[[Path.Combine(directory, "InRelease")], [Path.Combine(directory, "Release.gpg"), Path.Combine(directory, "Release")]])
        {
            var refused = Gpgv(tree, other.Public, file);
            Assert.NotEqual(0, refused.Exit);
            Assert.DoesNotContain("GOODSIG", refused.Said, StringComparison.Ordinal);
        }

        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(tree.Root, "t")));
    }

    /// <summary>
    /// The writer signs with the signing subkey alone, the secret the signing environment holds, and refuses before it
    /// writes anything when that is not what it was given: no secret at all, which is a run without the environment; a
    /// secret that holds the primary key's as well, which belongs offline; a public key alone, which holds no subkey that
    /// can sign; and two signing subkeys, where the release signs with one.
    /// </summary>
    [Theory]
    [InlineData("no secret", "APT_SIGNING_KEY is empty")]
    [InlineData("the primary's secret too", "holds the primary key's secret as well")]
    [InlineData("a public key alone", "holds no subkey that can sign")]
    [InlineData("two signing subkeys", "holds 2 subkeys that can sign")]
    public void The_writer_refuses_any_secret_but_the_signing_subkey(string given, string refusal)
    {
        RequireAptTools();
        using var tree = new TempTree();
        var (deb, _) = SmallDeb(tree, "1.2.3");
        var key = ThrowawayKey(tree, "3y", "3y");
        var secret = given switch
        {
            "no secret" => "",
            "the primary's secret too" => Tool("gpg", Home(key.Home), "--armor", "--export-secret-keys", key.Primary).Stdout,
            "a public key alone" => Tool("gpg", Home(key.Home), "--armor", "--export", key.Primary).Stdout,
            "two signing subkeys" => TwoSigningSubkeys(key),
            _ => throw new ArgumentOutOfRangeException(nameof(given)),
        };

        var (exit, said) = WriteAptIndex(tree, Path.GetDirectoryName(deb)!, secret);
        Assert.NotEqual(0, exit);
        Assert.Contains(refusal, said, StringComparison.Ordinal);
        Assert.Equal([Deb], Directory.EnumerateFiles(Path.GetDirectoryName(deb)!).Select(Path.GetFileName));
    }

    /// <summary>
    /// The check passes the index as written and refuses each copy a machine's apt would not take, or would take with
    /// a complaint, in the words of the check that caught it: a deb changed after its index was written, a Packages
    /// changed after Release was, a Release changed after it was signed, a missing signature, a signature by a key that
    /// is not the one given, a signature by a key revoked since or one that has expired, which gpgv exits 0 for, and a
    /// signature gpgv takes and apt refuses, a SHA-1 digest. The last is read by apt alone: gpgv says the signature is good.
    /// </summary>
    [Theory]
    [InlineData("as written", null)]
    // Critical: the guard of a release's deb against the index that names it, on every pull request's Linux tier.
    [InlineData("the deb changed after the index was written", "Packages says SHA256 ", Traits = ["Category", "Critical", "CriticalFamily", nameof(Family.I1)])]
    [InlineData("Packages changed after Release was written", "Release says Packages has SHA256 ")]
    [InlineData("Release changed after it was signed", "InRelease does not sign the text of Release")]
    [InlineData("no Release.gpg", "Release.gpg is not there")]
    [InlineData("signed by another key", "InRelease is not signed by a key in ")]
    [InlineData("signed by a key since revoked", "InRelease's signature is not good")]
    [InlineData("signed by a key since expired", "InRelease's signature is not good")]
    [InlineData("a digest apt refuses", "apt-get update did not read the index without a word")]
    public void The_check_refuses_an_index_a_machine_s_apt_would_not_take(string copy, string? refusal)
    {
        RequireAptTools();
        using var tree = new TempTree();
        var (deb, _) = SmallDeb(tree, "1.2.3~rc.1");
        var directory = Path.GetDirectoryName(deb)!;
        var key = ThrowawayKey(tree, "3y", "3y");
        var written = WriteAptIndex(tree, directory, key.Secret);
        Assert.True(written.Exit == 0, written.Said);

        var keyring = key.Public;
        switch (copy)
        {
            case "as written":
                break;
            case "the deb changed after the index was written":
                var bytes = File.ReadAllBytes(deb);
                bytes[^1] ^= 0xFF;
                File.WriteAllBytes(deb, bytes);
                break;
            case "Packages changed after Release was written":
                File.AppendAllText(Path.Combine(directory, "Packages"), "Tag: changed\n");
                break;
            case "Release changed after it was signed":
                File.AppendAllText(Path.Combine(directory, "Release"), "Suite: changed\n");
                break;
            case "no Release.gpg":
                File.Delete(Path.Combine(directory, "Release.gpg"));
                break;
            case "signed by another key":
                keyring = ThrowawayKey(tree, "3y", "3y").Public;
                break;
            case "signed by a key since revoked":
                var certificate = File.ReadAllText(Path.Combine(key.Home, "openpgp-revocs.d", key.Primary + ".rev")).Replace(":-----BEGIN", "-----BEGIN", StringComparison.Ordinal);
                var revocation = Path.Combine(tree.Root, "revocation.asc");
                File.WriteAllText(revocation, certificate);
                Assert.Equal(0, Tool("gpg", Home(key.Home), "--batch", "--import", revocation).Exit);
                Assert.Equal(0, Tool("gpg", Home(key.Home), "--batch", "--yes", "--output", keyring, "--export", key.Primary).Exit);
                Tool("gpgconf", Home(key.Home), "--kill", "all");
                Assert.Contains("[GNUPG:] REVKEYSIG ", Gpgv(tree, keyring, Path.Combine(directory, "InRelease")).Said, StringComparison.Ordinal);
                break;
            case "signed by a key since expired":
                // Made four days ago with two days to live, and signing the index as it stood then.
                var then = DateTimeOffset.UtcNow.AddDays(-4).ToUnixTimeSeconds();
                var expired = ThrowawayKey(tree, "2d", "2d", then);
                keyring = expired.Public;
                SignAt(expired, directory, then + 60);
                var verdict = Gpgv(tree, keyring, Path.Combine(directory, "InRelease"));
                Assert.Contains("[GNUPG:] EXPKEYSIG ", verdict.Said, StringComparison.Ordinal);
                Assert.Equal(0, verdict.Exit);
                break;
            case "a digest apt refuses":
                foreach (var how in (string[][])[["--clearsign", "--output", Path.Combine(directory, "InRelease")], ["--armor", "--detach-sign", "--output", Path.Combine(directory, "Release.gpg")]])
                {
                    var signed = Tool("gpg", Home(key.Home), ["--batch", "--yes", "--local-user", key.Subkey + "!", "--digest-algo", "SHA1", .. how, Path.Combine(directory, "Release")]);
                    Assert.True(signed.Exit == 0, signed.Stderr);
                }

                Assert.Contains("[GNUPG:] GOODSIG ", Gpgv(tree, keyring, Path.Combine(directory, "InRelease")).Said, StringComparison.Ordinal);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(copy));
        }

        var (exit, said) = CheckAptIndex(tree, directory, keyring);
        if (refusal is null)
        {
            Assert.True(exit == 0, said);
            Assert.Contains("offers jason-desktop 1.2.3~rc.1", said, StringComparison.Ordinal);
        }
        else
        {
            Assert.True(exit != 0, $"{copy}: the check passed it.\n{said}");
            Assert.Contains(refusal, said, StringComparison.Ordinal);
        }

        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(tree.Root, "t")));
    }

    /// <summary>
    /// The key check, as the release's apt-index job runs it on the repository's key right before it signs: a key whose
    /// primary and signing subkey are each more than 90 days from their expiry passes and says when each expires; a
    /// primary or a signing subkey within those 90 days, one already expired, and one revoked each fail, naming the key,
    /// and the date where there is one, with no index and no signature to read.
    /// </summary>
    [Theory]
    [InlineData("passes", "3y", "3y")]
    [InlineData("primary", "89d", "3y")]
    [InlineData("subkey", "3y", "89d")]
    [InlineData("expired", "2d", "2d")]
    [InlineData("revoked", "3y", "3y")]
    public void The_key_check_holds_a_keyring_to_its_expiry(string kind, string primary, string subkey)
    {
        RequireAptTools();
        using var tree = new TempTree();
        var key = ThrowawayKey(tree, primary, subkey, kind == "expired" ? DateTimeOffset.UtcNow.AddDays(-4).ToUnixTimeSeconds() : null);
        if (kind == "revoked")
        {
            var certificate = File.ReadAllText(Path.Combine(key.Home, "openpgp-revocs.d", key.Primary + ".rev")).Replace(":-----BEGIN", "-----BEGIN", StringComparison.Ordinal);
            var revocation = Path.Combine(tree.Root, "revocation.asc");
            File.WriteAllText(revocation, certificate);
            Assert.Equal(0, Tool("gpg", Home(key.Home), "--batch", "--import", revocation).Exit);
            Assert.Equal(0, Tool("gpg", Home(key.Home), "--batch", "--yes", "--output", key.Public, "--export", key.Primary).Exit);
            Tool("gpgconf", Home(key.Home), "--kill", "all");
        }

        var (exit, stdout, stderr) = Tool("bash", Scratch(tree), Path.Combine([RepositoryRoot(), .. AptKeyCheck]), key.Public);
        var said = stdout + stderr;
        var which = kind == "primary" ? $"The apt repository's key {key.Primary}" : $"The apt repository's signing subkey {key.Subkey}";
        switch (kind)
        {
            case "passes":
                Assert.True(exit == 0, said);
                Assert.Contains($"{which} expires on {Expiry(tree, key, key.Subkey)}", said, StringComparison.Ordinal);
                Assert.Contains("more than 90 days from this run", said, StringComparison.Ordinal);
                break;
            case "revoked":
                Assert.True(exit != 0, said);
                Assert.Contains($"The apt repository's key {key.Primary} is revoked: apt refuses an index it signs", said, StringComparison.Ordinal);
                break;
            default:
                Assert.True(exit != 0, said);
                var verb = kind == "expired" ? "expired on" : "expires on";
                Assert.Contains($"{which} {verb} {Expiry(tree, key, kind == "primary" ? key.Primary : key.Subkey)}, within 90 days", said, StringComparison.Ordinal);
                Assert.Contains("must be extended or replaced, and the new public key shipped in a release's deb, before then", said, StringComparison.Ordinal);
                break;
        }

        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(tree.Root, "t")));
    }

    /// <summary>InRelease and Release.gpg signed again by <paramref name="key"/>'s subkey as the clock stood at <paramref name="at"/>.</summary>
    private static void SignAt(AptKey key, string directory, long at)
    {
        foreach (var how in (string[][])[["--clearsign", "--output", Path.Combine(directory, "InRelease")], ["--armor", "--detach-sign", "--output", Path.Combine(directory, "Release.gpg")]])
        {
            var signed = Tool("gpg", Home(key.Home), ["--batch", "--yes", "--faked-system-time", $"{at}!", "--local-user", key.Subkey + "!", .. how, Path.Combine(directory, "Release")]);
            Assert.True(signed.Exit == 0, signed.Stderr);
        }
    }

    /// <summary>The key's secret subkeys once a second signing subkey stands beside the first.</summary>
    private static string TwoSigningSubkeys(AptKey key)
    {
        var added = Tool("gpg", Home(key.Home), "--batch", "--passphrase", "", "--quick-add-key", key.Primary, "ed25519", "sign", "3y");
        Assert.True(added.Exit == 0, added.Stderr);
        var secret = Tool("gpg", Home(key.Home), "--batch", "--armor", "--export-secret-subkeys", key.Primary).Stdout;
        Tool("gpgconf", Home(key.Home), "--kill", "all");
        return secret;
    }

    /// <summary>A key's expiry date, read from the key itself, as the check names it.</summary>
    private static string Expiry(TempTree tree, AptKey key, string fingerprint)
    {
        var listed = Tool("gpg", Home(Path.Combine(tree.Root, "list")), "--batch", "--show-keys", "--with-colons", key.Public).Stdout.Split('\n');
        var at = Array.FindIndex(listed, line => line == $"fpr:::::::::{fingerprint}:");
        Assert.True(at > 0, $"{fingerprint} is not in {key.Public}.");
        var seconds = long.Parse(listed[at - 1].Split(':')[6], CultureInfo.InvariantCulture);
        return DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// A key shaped as the release's: an Ed25519 key that certifies, with an Ed25519 subkey that signs, each with its own
    /// lifetime, made in a home of its own under the tree, at <paramref name="madeAt"/> when given. Its public half is a
    /// binary keyring file; its secret is the signing subkey alone, armored, as the signing environment holds it.
    /// </summary>
    private sealed record AptKey(string Home, string Public, string Secret, string Primary, string Subkey);

    private static int keys;

    private static AptKey ThrowawayKey(TempTree tree, string primary, string subkey, long? madeAt = null)
    {
        // Under the tree's root itself: gpg puts its agent's socket in the home where the system has no run directory
        // for the account, and a socket's path may be at most 107 bytes.
        var home = Path.Combine(tree.Root, $"k{Interlocked.Increment(ref keys)}");
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(home);
        }
        else
        {
            Directory.CreateDirectory(home, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        string[] time = madeAt is { } at ? ["--faked-system-time", $"{at}!"] : [];
        var made = Tool("gpg", Home(home), ["--batch", .. time, "--passphrase", "", "--quick-generate-key", "Throwaway apt key <apt@example.invalid>", "ed25519", "cert", primary]);
        Assert.True(made.Exit == 0, made.Stderr);
        var fingerprint = Tool("gpg", Home(home), "--batch", "--with-colons", "--list-keys").Stdout.Split('\n').First(line => line.StartsWith("fpr:", StringComparison.Ordinal)).Split(':')[9];
        var added = Tool("gpg", Home(home), ["--batch", .. time, "--passphrase", "", "--quick-add-key", fingerprint, "ed25519", "sign", subkey]);
        Assert.True(added.Exit == 0, added.Stderr);

        var listed = Tool("gpg", Home(home), "--batch", "--with-colons", "--list-keys", fingerprint).Stdout.Split('\n');
        var sub = listed[Array.FindIndex(listed, line => line.StartsWith("sub:", StringComparison.Ordinal)) + 1].Split(':')[9];
        var keyring = Path.Combine(home, "public.gpg");
        Assert.Equal(0, Tool("gpg", Home(home), "--batch", "--output", keyring, "--export", fingerprint).Exit);
        var secret = Tool("gpg", Home(home), "--batch", "--armor", "--export-secret-subkeys", fingerprint).Stdout;
        Tool("gpgconf", Home(home), "--kill", "all");
        return new AptKey(home, keyring, secret, fingerprint, sub);
    }

    /// <summary>
    /// A deb of the package's name, built by dpkg-deb from a control file of the test's own whose description runs over
    /// two lines, beside nothing else in a folder of its own, under the name the release publishes it by. Returns the
    /// file and its control text as written.
    /// </summary>
    private static (string Deb, string Control) SmallDeb(TempTree tree, string version)
    {
        var root = Path.Combine(tree.NewDirectory("package"), "root");
        Directory.CreateDirectory(Path.Combine(root, "DEBIAN"));
        Directory.CreateDirectory(Path.Combine(root, "opt", "Jason"));
        File.WriteAllText(Path.Combine(root, "opt", "Jason", "README"), "not really the application\n");
        var control = $"Package: jason-desktop\nVersion: {version}\nArchitecture: amd64\nMaintainer: Jason Tests <tests@example.invalid>\nInstalled-Size: 1\nDescription: not really the desktop\n a test's own package, two lines long\n";
        File.WriteAllText(Path.Combine(root, "DEBIAN", "control"), control);

        var deb = Path.Combine(tree.NewDirectory("dist"), Deb);
        var built = Tool("dpkg-deb", NoEnvironment, "--root-owner-group", "--build", root, deb);
        Assert.True(built.Exit == 0, built.Stderr);
        return (deb, control);
    }

    private static (int Exit, string Said) WriteAptIndex(TempTree tree, string directory, string secret)
    {
        var environment = new Dictionary<string, string>(Scratch(tree)) { [AptSecret] = secret };
        var (exit, stdout, stderr) = Tool("bash", environment, Path.Combine([RepositoryRoot(), .. AptWriter]), directory);
        return (exit, stdout + stderr);
    }

    private static (int Exit, string Said) CheckAptIndex(TempTree tree, string directory, string keyring)
    {
        var (exit, stdout, stderr) = Tool("bash", Scratch(tree), Path.Combine([RepositoryRoot(), .. AptChecker]), directory, keyring);
        return (exit, stdout + stderr);
    }

    /// <summary>gpgv over the files given, with the keyring given and nothing else, its status lines and its words together.</summary>
    private static (int Exit, string Said) Gpgv(TempTree tree, string keyring, params string[] files)
    {
        var (exit, stdout, stderr) = Tool("gpgv", Home(Path.Combine(tree.Root, "gpgv")), ["--status-fd", "1", "--keyring", keyring, .. files]);
        return (exit, stdout + stderr);
    }

    /// <summary>
    /// The scripts' temporary folders, under the tree's own <c>t</c>, so a test sees what they leave behind; short, so the
    /// signing home's agent socket fits.
    /// </summary>
    private static Dictionary<string, string> Scratch(TempTree tree)
    {
        var scratch = Path.Combine(tree.Root, "t");
        Directory.CreateDirectory(scratch);
        return new Dictionary<string, string> { ["TMPDIR"] = scratch, ["GNUPGHOME"] = Path.Combine(tree.Root, "never") };
    }

    /// <summary>A gpg home of the test's own, never the account's.</summary>
    private static Dictionary<string, string> Home(string home)
    {
        Directory.CreateDirectory(home);
        return new Dictionary<string, string> { ["GNUPGHOME"] = home };
    }

    /// <summary>
    /// dpkg-deb, gpg, gpgv and apt-get, which a Debian or an Ubuntu system has and this suite installs nothing to get.
    /// Elsewhere these rows skip and say so; on a GitHub-hosted Linux runner, an Ubuntu that has every one, a missing tool
    /// fails them, so the leg that is meant to run them never skips them unseen.
    /// </summary>
    private static void RequireAptTools()
    {
        var missing = new[] { "dpkg-deb", "gpg", "gpgv", "gpgconf", "apt-get", "apt-cache" }.Where(tool => OnPath(tool) is null).ToList();
        if (missing.Count == 0)
        {
            return;
        }

        var hosted = OperatingSystem.IsLinux()
            && Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true"
            && Environment.GetEnvironmentVariable("RUNNER_ENVIRONMENT") == "github-hosted";
        Assert.False(hosted, $"{string.Join(", ", missing)} not on this runner's PATH, and an Ubuntu runner has every one.");
        Assert.Skip($"{string.Join(", ", missing)} not on this machine's PATH: the apt index is written and read with a Debian system's tools, and CI's Linux leg runs this row.");
    }

    private static string? OnPath(string tool) =>
        (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(directory => Path.Combine(directory, tool))
            .FirstOrDefault(File.Exists);

    /// <summary>A program run to completion with the environment given over this process's, bounded at two minutes.</summary>
    private static (int Exit, string Stdout, string Stderr) Tool(string program, IReadOnlyDictionary<string, string> environment, params string[] args)
    {
        var start = new ProcessStartInfo(program)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in args)
        {
            start.ArgumentList.Add(argument);
        }

        start.Environment["LC_ALL"] = "C";
        foreach (var (name, value) in environment)
        {
            start.Environment[name] = value;
        }

        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(TimeSpan.FromSeconds(120)))
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail($"{program} did not finish in two minutes.");
        }

        return (process.ExitCode, stdout.Result, stderr.Result);
    }
}
