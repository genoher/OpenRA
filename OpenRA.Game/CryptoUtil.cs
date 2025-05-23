#region Copyright & License Information
/*
 * Copyright (c) The OpenRA Developers and Contributors
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace OpenRA
{
	public static class CryptoUtil
	{
		// Fixed byte pattern for the OID header
		static readonly byte[] OIDHeader = [0x30, 0xD, 0x6, 0x9, 0x2A, 0x86, 0x48, 0x86, 0xF7, 0xD, 0x1, 0x1, 0x1, 0x5, 0x0];

		static readonly char[] HexUpperAlphabet = "0123456789ABCDEF".ToArray();
		static readonly char[] HexLowerAlphabet = "0123456789abcdef".ToArray();

		public static string PublicKeyFingerprint(RSAParameters parameters)
		{
			// Public key fingerprint is defined as the SHA1 of the modulus + exponent bytes
			return SHA1Hash(parameters.Modulus.Append(parameters.Exponent).ToArray());
		}

		public static string EncodePEMPublicKey(RSAParameters parameters)
		{
			var data = Convert.ToBase64String(EncodePublicKey(parameters));
			var output = new StringBuilder();
			output.AppendLine("-----BEGIN PUBLIC KEY-----");
			for (var i = 0; i < data.Length; i += 64)
				output.AppendLine(data.Substring(i, Math.Min(64, data.Length - i)));
			output.Append("-----END PUBLIC KEY-----");

			return output.ToString();
		}

		public static RSAParameters DecodePEMPublicKey(string key)
		{
			try
			{
				// Reconstruct original key data
				var lines = key.Split('\n');
				var data = Convert.FromBase64String(lines.Skip(1).Take(lines.Length - 2).JoinWith(""));

				// Pull the modulus and exponent bytes out of the ASN.1 tree
				// Expect this to blow up if the key is not correctly formatted
				using (var s = new MemoryStream(data))
				{
					// SEQUENCE
					s.ReadUInt8();
					ReadTLVLength(s);

					// SEQUENCE -> fixed header junk
					s.ReadUInt8();
					var headerLength = ReadTLVLength(s);
					s.Position += headerLength;

					// SEQUENCE -> BIT_STRING
					s.ReadUInt8();
					ReadTLVLength(s);
					s.ReadUInt8();

					// SEQUENCE -> BIT_STRING -> SEQUENCE
					s.ReadUInt8();
					ReadTLVLength(s);

					// SEQUENCE -> BIT_STRING -> SEQUENCE -> INTEGER (modulus)
					s.ReadUInt8();
					var modulusLength = ReadTLVLength(s);
					s.ReadUInt8();
					var modulus = s.ReadBytes(modulusLength - 1);

					// SEQUENCE -> BIT_STRING -> SEQUENCE -> INTEGER (exponent)
					s.ReadUInt8();
					var exponentLength = ReadTLVLength(s);
					s.ReadUInt8();
					var exponent = s.ReadBytes(exponentLength - 1);

					return new RSAParameters
					{
						Modulus = modulus,
						Exponent = exponent
					};
				}
			}
			catch (Exception e)
			{
				throw new InvalidDataException("Invalid PEM public key", e);
			}
		}

		static byte[] EncodePublicKey(RSAParameters parameters)
		{
			using (var stream = new MemoryStream())
			{
				var writer = new BinaryWriter(stream);

				var modExpLength = TripletFullLength(parameters.Modulus.Length + 1) + TripletFullLength(parameters.Exponent.Length + 1);
				var bitStringLength = TripletFullLength(modExpLength + 1);
				var sequenceLength = TripletFullLength(bitStringLength + OIDHeader.Length);

				// SEQUENCE
				writer.Write((byte)0x30);
				WriteTLVLength(writer, sequenceLength);

				// SEQUENCE -> fixed header junk
				writer.Write(OIDHeader);

				// SEQUENCE -> BIT_STRING
				writer.Write((byte)0x03);
				WriteTLVLength(writer, bitStringLength);
				writer.Write((byte)0x00);

				// SEQUENCE -> BIT_STRING -> SEQUENCE
				writer.Write((byte)0x30);
				WriteTLVLength(writer, modExpLength);

				// SEQUENCE -> BIT_STRING -> SEQUENCE -> INTEGER
				// Modulus is padded with a zero to avoid issues with the sign bit
				writer.Write((byte)0x02);
				WriteTLVLength(writer, parameters.Modulus.Length + 1);
				writer.Write((byte)0);
				writer.Write(parameters.Modulus);

				// SEQUENCE -> BIT_STRING -> SEQUENCE -> INTEGER
				// Exponent is padded with a zero to avoid issues with the sign bit
				writer.Write((byte)0x02);
				WriteTLVLength(writer, parameters.Exponent.Length + 1);
				writer.Write((byte)0);
				writer.Write(parameters.Exponent);

				return stream.ToArray();
			}
		}

		static void WriteTLVLength(BinaryWriter writer, int length)
		{
			if (length < 0x80)
			{
				// Length < 128 is stored in a single byte
				writer.Write((byte)length);
			}
			else
			{
				// If 128 <= length < 256**128 first byte encodes number of bytes required to hold the length
				// High-bit is set as a flag to use this long-form encoding
				var lengthBytes = BitConverter.GetBytes(length).Reverse().SkipWhile(b => b == 0).ToArray();
				writer.Write((byte)(0x80 | lengthBytes.Length));
				writer.Write(lengthBytes);
			}
		}

		static int ReadTLVLength(Stream s)
		{
			var length = s.ReadUInt8();
			if (length < 0x80)
				return length;

			Span<byte> data = stackalloc byte[4];
			s.ReadBytes(data[..Math.Min(length & 0x7F, 4)]);
			return BitConverter.ToInt32(data);
		}

		static int TripletFullLength(int dataLength)
		{
			if (dataLength < 0x80)
				return 2 + dataLength;

			return 2 + dataLength + BitConverter.GetBytes(dataLength).Reverse().SkipWhile(b => b == 0).Count();
		}

		public static string DecryptString(RSAParameters parameters, string data)
		{
			try
			{
				using (var rsa = new RSACryptoServiceProvider())
				{
					rsa.ImportParameters(parameters);
					return Encoding.UTF8.GetString(rsa.Decrypt(Convert.FromBase64String(data), false));
				}
			}
			catch (Exception e)
			{
				Log.Write("debug", "Failed to decrypt string with exception:");
				Log.Write("debug", e);
				Console.WriteLine("String decryption failed:");
				Console.WriteLine(e);
				return null;
			}
		}

		public static string Sign(RSAParameters parameters, string data)
		{
			return Sign(parameters, Encoding.UTF8.GetBytes(data));
		}

		public static string Sign(RSAParameters parameters, byte[] data)
		{
			try
			{
				using (var rsa = new RSACryptoServiceProvider())
				{
					rsa.ImportParameters(parameters);
					return Convert.ToBase64String(rsa.SignHash(SHA1.HashData(data), CryptoConfig.MapNameToOID("SHA1")));
				}
			}
			catch (Exception e)
			{
				Log.Write("debug", "Failed to sign string with exception");
				Log.Write("debug", e);
				Console.WriteLine("String signing failed:");
				Console.WriteLine(e);
				return null;
			}
		}

		public static bool VerifySignature(RSAParameters parameters, string data, string signature)
		{
			return VerifySignature(parameters, Encoding.UTF8.GetBytes(data), signature);
		}

		public static bool VerifySignature(RSAParameters parameters, byte[] data, string signature)
		{
			try
			{
				using (var rsa = new RSACryptoServiceProvider())
				{
					rsa.ImportParameters(parameters);
					return rsa.VerifyHash(SHA1.HashData(data), CryptoConfig.MapNameToOID("SHA1"), Convert.FromBase64String(signature));
				}
			}
			catch (Exception e)
			{
				Log.Write("debug", "Failed to verify signature with exception:");
				Log.Write("debug", e);
				Console.WriteLine("Signature validation failed:");
				Console.WriteLine(e);
				return false;
			}
		}

		public static string SHA1Hash(Stream data)
		{
			return ToHex(SHA1.HashData(data), true);
		}

		public static string SHA1Hash(byte[] data)
		{
			return ToHex(SHA1.HashData(data), true);
		}

		public static string SHA1Hash(string data)
		{
			return SHA1Hash(Encoding.UTF8.GetBytes(data));
		}

		public static string ToHex(ReadOnlySpan<byte> source, bool lowerCase = false)
		{
			if (source.Length == 0)
				return string.Empty;

			// excessively avoid stack overflow if source is too large (considering that we're allocating a new string)
			var buffer = source.Length <= 256 ? stackalloc char[source.Length * 2] : new char[source.Length * 2];
			return ToHexInternal(source, buffer, lowerCase);
		}

		static string ToHexInternal(ReadOnlySpan<byte> source, Span<char> buffer, bool lowerCase)
		{
			var sourceIndex = 0;
			var alphabet = lowerCase ? HexLowerAlphabet : HexUpperAlphabet;

			for (var i = 0; i < buffer.Length; i += 2)
			{
				var b = source[sourceIndex++];
				buffer[i] = alphabet[b >> 4];
				buffer[i + 1] = alphabet[b & 0xF];
			}

			return new string(buffer);
		}
	}
}
