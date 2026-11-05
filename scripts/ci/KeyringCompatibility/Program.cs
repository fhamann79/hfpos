using Microsoft.AspNetCore.DataProtection;

var emit = args is ["emit"];
if ((!emit && args is not ["read"]) || Environment.Version.Major != (emit ? 8 : 10))
    throw new InvalidOperationException("Expected RC6 .NET 8 emitter or candidate .NET 10 reader.");

const string purpose = "hfpos.company-email.smtp-password.v1";
const string plaintext = "DEV-532 synthetic payload, not a credential";
const string payloadPath = "payload";
var keyring = new DirectoryInfo("keys");
if (emit)
    keyring.Create();

var provider = DataProtectionProvider.Create(keyring, builder =>
{
    builder.SetApplicationName("HFPOS");
    if (!emit)
        builder.DisableAutomaticKeyGeneration();
});
var protector = provider.CreateProtector(purpose);
if (emit)
    File.WriteAllText(payloadPath, protector.Protect(plaintext));
else if (protector.Unprotect(File.ReadAllText(payloadPath)) != plaintext)
    throw new InvalidOperationException("Candidate did not recover the expected synthetic payload.");

Console.WriteLine(emit ? "RC6 KEYRING WRITE PASS" : "CANDIDATE KEYRING READ PASS");
