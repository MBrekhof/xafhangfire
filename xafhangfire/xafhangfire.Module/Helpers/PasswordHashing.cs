using DevExpress.Persistent.Base;

namespace xafhangfire.Module.Helpers
{
    // UPG-001 (decision 2a): DX 26.1's Latest compatibility mode hashes with SHA512-600K only, so passwords stored
    // as SHA1-20K would stop verifying. Keep verifying both; new and changed passwords get SHA512-600K.
    // Call after FrameworkSettings.DefaultSettingsCompatibilityMode is set (both apps share the database).
    public static class PasswordHashing
    {
        public static void UseMixed()
        {
            PasswordCryptographer.UseSHA1_20K = true;
            PasswordCryptographer.UseSHA512_600K = true;
        }
    }
}
