using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace MapSystem
{
    /// <summary>
    /// Ed25519 sign/verify dla map v8 (FORMAP04, Pillar 2 — provenance + integralność). Port z
    /// D:\Gry\formap\Signing.cs (BouncyCastle.Cryptography 2.4.0, ten sam DLL co po stronie generatora).
    ///
    /// Produkcyjnie gra używa TYLKO <see cref="Verify"/>; <see cref="Sign"/> + <see cref="GenerateKeypair"/>
    /// są dla testów round-trip (EditMode) — pozwalają testowi podpisać syntetyczny indeks bez bezpośredniej
    /// referencji do BouncyCastle (idzie przez ten wrapper w RailwayManager.Map).
    ///
    /// Klucze to surowe 32-bajtowe wartości Ed25519 (priv seed / pub), podpis = 64 bajty.
    /// </summary>
    public static class MapSigning
    {
        public const int PrivateKeySize = 32;
        public const int PublicKeySize = 32;
        public const int SignatureSize = 64;

        /// <summary>Weryfikuje 64-bajtowy podpis Ed25519 nad <paramref name="data"/> przeciw 32-bajtowemu kluczowi publicznemu.</summary>
        public static bool Verify(byte[] publicKey32, byte[] data, byte[] sig64)
        {
            var pub = new Ed25519PublicKeyParameters(publicKey32, 0);
            var verifier = new Ed25519Signer();
            verifier.Init(forSigning: false, pub);
            verifier.BlockUpdate(data, 0, data.Length);
            return verifier.VerifySignature(sig64);
        }

        /// <summary>Podpis Ed25519 (64 B) nad <paramref name="data"/> przy 32-bajtowym private seed. (Test/tooling.)</summary>
        public static byte[] Sign(byte[] privateKey32, byte[] data)
        {
            var priv = new Ed25519PrivateKeyParameters(privateKey32, 0);
            var signer = new Ed25519Signer();
            signer.Init(forSigning: true, priv);
            signer.BlockUpdate(data, 0, data.Length);
            return signer.GenerateSignature();
        }

        /// <summary>Generuje świeżą parę kluczy Ed25519 (surowe 32-bajtowe priv/pub). (Test/tooling.)</summary>
        public static void GenerateKeypair(out byte[] privateKey32, out byte[] publicKey32)
        {
            var random = new Org.BouncyCastle.Security.SecureRandom();
            var priv = new Ed25519PrivateKeyParameters(random);
            var pub = priv.GeneratePublicKey();
            privateKey32 = priv.GetEncoded();
            publicKey32 = pub.GetEncoded();
        }
    }
}
