﻿using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using cs_implementation;

namespace openaes
{
    class Program
    {
        public static async Task<int> Main(params string[] args)
        {
            RootCommand rootCommand = new RootCommand(
                description: "Encrypts (default) or decrypts a file using Advanced Encryption Standard (AES-128-ECB).");
            Option inputOption = new Option(
                aliases: new string[] { "--input", "-i" },
                description: "The path to the file that is to be encrypted/decrypted.");
            inputOption.Argument = new Argument<FileInfo>();
            inputOption.IsRequired = true;
            rootCommand.AddOption(inputOption);
            Option outputOption = new Option(
                aliases: new string[] { "--output", "-o" },
                description: "The target name of the output file after encryption/decryption.");
            outputOption.Argument = new Argument<FileInfo>();
            outputOption.IsRequired = true;
            rootCommand.AddOption(outputOption);
            Option passOption = new Option(
                aliases: new string[] { "--passphrase", "-p" },
                description: "The passphrase to derive the key from.");
            passOption.Argument = new Argument<String>();
            passOption.IsRequired = true;
            rootCommand.AddOption(passOption);
            Option decryptOption = new Option(
                aliases: new string[] { "--decrypt", "-d" },
                description: "Decrypt the input data.");
            decryptOption.Argument = new Argument<bool>();
            rootCommand.AddOption(decryptOption);
            Option implementationOption = new Option(
                aliases: new string[] { "--use", "-u" },
                description: "Choose which implementation to use (Asm by default)");
            implementationOption.Argument = new Argument<AesImplementation>();
            rootCommand.AddOption(implementationOption);
            rootCommand.Handler =
              CommandHandler.Create<FileInfo, FileInfo, String, bool, AesImplementation>(RunAES);
            return await rootCommand.InvokeAsync(args);
        }

        public static void RunAES(FileInfo input, FileInfo output, String passphrase, bool decrypt, AesImplementation use)
        {
            if(!input.Exists)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Error.WriteLine("Input file doesn't exist, exiting.");
                Console.ResetColor();
                return;
            }

            string operation = decrypt ? "decryption" : "encryption";
            Console.WriteLine($"Performing {operation} using the {use} implementation of the AES-128-ECB algorithm.");

            AesFactory aesFactory = new AesFactory();
            IAes aes = aesFactory.GetAes(use);

            var watch = System.Diagnostics.Stopwatch.StartNew();

            if (!decrypt)
            {
                // Create key using PBKDF2
                byte[] salt = new byte[8];
                int iterations = 10000;
                using (RNGCryptoServiceProvider rngCsp = new RNGCryptoServiceProvider())
                {
                    // Fill the array with a random value.
                    rngCsp.GetBytes(salt);
                }

                Rfc2898DeriveBytes key = new Rfc2898DeriveBytes(passphrase, salt, iterations, HashAlgorithmName.SHA256);

                using (FileStream fs = input.OpenRead())
                using (FileStream fsOut = output.OpenWrite())
                {
                    // Mark encrypted file as salted at beginning
                    string saltedMsg = "Salted__";
                    byte[] saltedMsgBytes = Encoding.ASCII.GetBytes(saltedMsg);
                    fsOut.Write(saltedMsgBytes);

                    // Insert salt into file
                    fsOut.Write(salt);

                    byte[] message = new byte[16];

                    byte[] expandedKey = new byte[176];

                    aes.KeyExpansion(key.GetBytes(16), expandedKey);

                    bool ended = false;

                    while (!ended)
                    {
                        int readBytes = fs.Read(message, 0, 16);
                        if (readBytes == 0) // pad 16 byte block
                        {
                            ended = true;
                            for (int i = 0; i < 16; i++)
                                message[i] = 16;
                        }
                        else if (readBytes % 16 != 0) // pad missing bytes according to PKCS#7
                        {
                            ended = true;
                            for (int i = 0; i < 16 - readBytes; i++)
                            {
                                message[readBytes + i] = (byte)(16 - readBytes);
                            }
                        }
                        aes.Encrypt(message, expandedKey);

                        fsOut.Write(message, 0, 16);
                    }
                }

            } else {
                using (FileStream fs = input.OpenRead())
                using (FileStream fsOut = output.Open(FileMode.Create, FileAccess.ReadWrite))
                {
                    byte[] salt = new byte[8];

                    fs.Read(salt, 0, 8); // Read first 8 bytes from input file
                    if (Encoding.ASCII.GetString(salt) == "Salted__") // Check if file is Salted
                    {
                        fs.Read(salt, 0, 8); // Read next 8 bytes into salt array
                    }

                    int iterations = 10000;

                    Rfc2898DeriveBytes key = new Rfc2898DeriveBytes(passphrase, salt, iterations, HashAlgorithmName.SHA256);

                    byte[] message = new byte[16];

                    byte[] expandedKey = new byte[176];

                    aes.KeyExpansion(key.GetBytes(16), expandedKey);

                    bool ended = false;

                    // Store previous decrypted block
                    byte[] final = new byte[16];

                    while(!ended)
                    {
                        int readBytes = fs.Read(message, 0, 16);
                        if(readBytes == 0)
                        {
                            ended = true;
                            // Check padding correctness
                            int n = final[15];
                            int b = 16;
                            if(n == 0 || n > 16)
                            {
                                Console.ForegroundColor = ConsoleColor.Red;
                                Console.Error.WriteLine("Bad decrypt (is the supplied passphrase correct?)");
                                Console.ResetColor();
                                return;
                            }
                            for(int i = 0; i<n; i++)
                            {
                                if(final[--b] != n)
                                {
                                    Console.ForegroundColor = ConsoleColor.Red;
                                    Console.Error.WriteLine("Bad decrypt (is the supplied passphrase correct?)");
                                    Console.ResetColor();
                                    return;
                                }
                            }

                            // Strip padding
                            fsOut.SetLength(fsOut.Length - n);
                            fsOut.Close();
                        }
                        else
                        {
                            aes.Decrypt(message, expandedKey);

                            for(int i = 0; i < 16; i++)
                            {
                                final[i] = message[i];
                            }
                            fsOut.Write(message, 0, 16);
                        }
                    }
                } 
            }

            watch.Stop();
            var elapsedMs = watch.ElapsedMilliseconds;
            Console.WriteLine($"The selected operation was performed in {elapsedMs} ms.");

        }
    }
}
