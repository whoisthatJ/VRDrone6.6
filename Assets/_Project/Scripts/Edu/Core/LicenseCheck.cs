using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace Ushqyr.Edu
{
    /// <summary>
    /// The two checks an offline licence rests on: the licence text was signed by the vendor's private key (the app
    /// holds only the public half), and it was issued for this PC. No network is used.
    /// </summary>
    public static class LicenseCheck
    {
        /// <summary>RSA signature (PKCS#1 v1.5, SHA-256) of the licence text against the built-in public key.</summary>
        public static bool SignatureValid(byte[] payload, byte[] signature, byte[] modulus, byte[] exponent)
        {
            if (payload == null || signature == null || modulus == null || exponent == null) return false;
            try
            {
                using (RSA rsa = RSA.Create())
                {
                    rsa.ImportParameters(new RSAParameters { Modulus = modulus, Exponent = exponent });
                    return rsa.VerifyData(payload, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                }
            }
            catch (CryptographicException)
            {
                return false;
            }
        }

        /// <summary>
        /// A PC's fingerprint: one short hash per hardware identifier, so the request file does not carry the
        /// identifiers themselves. Empty identifiers are left out.
        /// </summary>
        public static Dictionary<string, string> Fingerprint(IDictionary<string, string> identifiers)
        {
            var result = new Dictionary<string, string>();
            if (identifiers == null) return result;
            using (SHA256 sha = SHA256.Create())
            {
                foreach (KeyValuePair<string, string> id in identifiers)
                {
                    string value = (id.Value ?? "").Trim().ToLowerInvariant();
                    if (value.Length == 0) continue;
                    byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes("ushqyr-seat|" + id.Key + "|" + value));
                    var hex = new StringBuilder();
                    for (int i = 0; i < 8; i++) hex.Append(hash[i].ToString("x2"));
                    result[id.Key] = hex.ToString();
                }
            }
            return result;
        }

        /// <summary>
        /// How many identifiers of the licence are the same on this PC. The licence stays valid when a part was
        /// replaced: the caller asks for "at least N of those the licence names".
        /// </summary>
        public static int Matches(IDictionary<string, string> licensed, IDictionary<string, string> here)
        {
            int n = 0;
            if (licensed == null || here == null) return 0;
            foreach (KeyValuePair<string, string> id in licensed)
                if (here.TryGetValue(id.Key, out string v) && v == id.Value) n++;
            return n;
        }
    }
}
