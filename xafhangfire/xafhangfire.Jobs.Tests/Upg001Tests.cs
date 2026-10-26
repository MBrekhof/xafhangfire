using DevExpress.Persistent.Base;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using xafhangfire.Module.BusinessObjects;
using xafhangfire.Module.Helpers;

namespace xafhangfire.Jobs.Tests;

// UPG-001: the checks that replace Npgsql's legacy timestamp switch and DX 26.1's SHA512-only hashing.
public class Upg001Tests
{
    [Fact]
    public void EveryDateTimeProperty_IsTimestampWithoutTimeZone_AndDropsKind()
    {
        // Built the way XAF builds it (UseConnectionString adds the change-tracking proxies); no connection is opened.
        var options = new DbContextOptionsBuilder<xafhangfireEFCoreDbContext>();
        options.UseConnectionString("EFCoreProvider=PostgreSql;Host=localhost;Database=model-only");
        using var context = new xafhangfireEFCoreDbContext(options.Options);

        var properties = context.Model.GetEntityTypes()
            .SelectMany(e => e.GetProperties())
            .Where(p => p.ClrType == typeof(DateTime) || p.ClrType == typeof(DateTime?))
            .ToList();

        properties.Should().HaveCountGreaterThanOrEqualTo(8);
        // Every Kind stores its wall clock as written: no shift to UTC (a Local report EndDate must not move).
        var values = new[] { DateTimeKind.Utc, DateTimeKind.Local, DateTimeKind.Unspecified }
            .Select(k => new DateTime(2026, 10, 27, 12, 0, 0, k)).ToList();
        foreach (var p in properties)
        {
            var name = $"{p.DeclaringType.DisplayName()}.{p.Name}";
            p.GetColumnType().Should().Be("timestamp without time zone", name);
            var converter = p.GetValueConverter();
            converter.Should().BeOfType<UnspecifiedKindConverter>(name);
            foreach (var value in values)
            {
                var stored = (DateTime)converter!.ConvertToProvider(value)!;
                stored.Kind.Should().Be(DateTimeKind.Unspecified, name);
                stored.Ticks.Should().Be(value.Ticks, $"{name} ({value.Kind})");
                ((DateTime)converter.ConvertFromProvider(stored)!).Ticks.Should().Be(value.Ticks, name);
            }
        }
    }

    [Fact]
    public void MixedHashing_VerifiesExistingSha1Hashes_AndWritesSha512()
    {
        var cryptographer = new PasswordCryptographer();
        var (sha1, sha512) = (PasswordCryptographer.UseSHA1_20K, PasswordCryptographer.UseSHA512_600K);
        try
        {
            // A hash as DX 25.2 wrote it (SHA1-20K).
            PasswordCryptographer.UseSHA512_600K = false;
            PasswordCryptographer.UseSHA1_20K = true;
            var sha1Hash = cryptographer.GenerateSaltedPassword("s3cret");

            // Both off, so UseMixed has to switch both on (Latest mode starts with SHA1 off).
            PasswordCryptographer.UseSHA1_20K = false;
            PasswordHashing.UseMixed();
            cryptographer.AreEqual(sha1Hash, "s3cret").Should().BeTrue();
            cryptographer.AreEqual(sha1Hash, "wrong").Should().BeFalse();
            var newHash = cryptographer.GenerateSaltedPassword("s3cret");

            // The new hash is SHA512-600K: it verifies with SHA1 switched off, the old one doesn't.
            PasswordCryptographer.UseSHA1_20K = false;
            cryptographer.AreEqual(newHash, "s3cret").Should().BeTrue();
            cryptographer.AreEqual(sha1Hash, "s3cret").Should().BeFalse();
        }
        finally
        {
            (PasswordCryptographer.UseSHA1_20K, PasswordCryptographer.UseSHA512_600K) = (sha1, sha512);
        }
    }
}
