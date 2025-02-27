﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NBitcoin;
using Xels.SmartContracts.Core;

namespace Xels.SmartContracts.CLR.Serialization
{
    /// <summary>
    /// Class that handles method parameter serialization.
    /// </summary>
    public sealed class MethodParameterStringSerializer : IMethodParameterStringSerializer
    {
        private readonly Network network;

        public MethodParameterStringSerializer(Network network)
        {
            this.network = network;
        }

        /// <inheritdoc />
        public string Serialize(object methodParameter)
        {
            return this.SerializeObject(methodParameter);
        }

        /// <summary>
        /// Serializes an array of method parameter objects to the bytes of their string-encoded representation.
        /// </summary>
        public string Serialize(object[] methodParameters)
        {
            var sb = new List<string>();

            foreach (object obj in methodParameters)
            {
                sb.Add(this.SerializeObject(obj));
            }

            return this.EscapeAndJoin(sb.ToArray());
        }

        private string SerializeObject(object obj)
        {
            Prefix prefix = Prefix.ForObject(obj);

            string serialized = Serialize(obj, this.network);

            return string.Format("{0}#{1}", (int) prefix.DataType, serialized);
        }

        public static string Serialize(object obj, Network network)
        {
            MethodParameterDataType primitiveType = GetPrimitiveType(obj);

            // ToString works fine for all of our data types except byte arrays and addresses
            string serialized;

            switch (primitiveType)
            {
                case MethodParameterDataType.ByteArray:
                    serialized = ((byte[]) obj).ToHexString();
                    break;
                case MethodParameterDataType.Address:
                    serialized = ((Stratis.SmartContracts.Address) obj).ToUint160().ToBase58Address(network);
                    break;
                default:
                    serialized = obj.ToString();
                    break;
            }

            return serialized;
        }

        private static MethodParameterDataType GetPrimitiveType(object o)
        {
            return o switch
            {
                bool _ => MethodParameterDataType.Bool,
                byte _ => MethodParameterDataType.Byte,
                byte[] _ => MethodParameterDataType.ByteArray,
                char _ => MethodParameterDataType.Char,
                string _ => MethodParameterDataType.String,
                uint _ => MethodParameterDataType.UInt,
                ulong _ => MethodParameterDataType.ULong,
                Stratis.SmartContracts.Address _ => MethodParameterDataType.Address,
                long _ => MethodParameterDataType.Long,
                int _ => MethodParameterDataType.Int,
                Stratis.SmartContracts.UInt128 _ => MethodParameterDataType.UInt128,
                Stratis.SmartContracts.UInt256 _ => MethodParameterDataType.UInt256,
                _ => throw new MethodParameterStringSerializerException(string.Format("{0} is not supported.", o.GetType().Name))
            };
        }

        public object[] Deserialize(string[] parameters)
        {
            return this.StringToObjects(this.EscapeAndJoin(parameters));
        }

        public object[] Deserialize(string parameters)
        {
            return this.StringToObjects(parameters);
        }

        private object[] StringToObjects(string parameters)
        {
            string[] split = Regex.Split(parameters, @"(?<!(?<!\\)*\\)\|").ToArray();

            var processedParameters = new List<object>();

            foreach (string parameter in split)
            {
                string parameterType = "";
                string parameterValue = "";

                try
                {
                    string[] parameterSignature =
                        Regex.Split(parameter.Replace(@"\|", "|"), @"(?<!(?<!\\)*\\)\#").ToArray();
                    parameterSignature[1] = parameterSignature[1].Replace(@"\#", "#");

                    parameterType = ulong.TryParse(parameterSignature[0], out var parsedParameterType) 
                        ? Enum.GetName(typeof(MethodParameterDataType), parsedParameterType) ?? parameterSignature[0]
                        : parameterSignature[0];

                    parameterValue = parameterSignature[1];

                    if (parameterSignature[0] == MethodParameterDataType.Bool.ToString("d"))
                        processedParameters.Add(bool.Parse(parameterSignature[1]));

                    else if (parameterSignature[0] == MethodParameterDataType.Byte.ToString("d"))
                        processedParameters.Add(Convert.ToByte(parameterSignature[1]));

                    else if (parameterSignature[0] == MethodParameterDataType.Char.ToString("d"))
                        processedParameters.Add(parameterSignature[1][0]);

                    else if (parameterSignature[0] == MethodParameterDataType.String.ToString("d"))
                        processedParameters.Add(parameterSignature[1]);

                    else if (parameterSignature[0] == MethodParameterDataType.UInt.ToString("d"))
                        processedParameters.Add(uint.Parse(parameterSignature[1]));

                    else if (parameterSignature[0] == MethodParameterDataType.Int.ToString("d"))
                        processedParameters.Add(int.Parse(parameterSignature[1]));

                    else if (parameterSignature[0] == MethodParameterDataType.ULong.ToString("d"))
                        processedParameters.Add(ulong.Parse(parameterSignature[1]));

                    else if (parameterSignature[0] == MethodParameterDataType.Long.ToString("d"))
                        processedParameters.Add(long.Parse(parameterSignature[1]));

                    else if (parameterSignature[0] == MethodParameterDataType.Address.ToString("d"))
                        processedParameters.Add(parameterSignature[1].ToAddress(this.network));

                    else if (parameterSignature[0] == MethodParameterDataType.ByteArray.ToString("d"))
                        processedParameters.Add(parameterSignature[1].HexToByteArray());

                    else if (parameterSignature[0] == MethodParameterDataType.UInt128.ToString("d"))
                        processedParameters.Add(Stratis.SmartContracts.UInt128.Parse(parameterSignature[1]));

                    else if (parameterSignature[0] == MethodParameterDataType.UInt256.ToString("d"))
                        processedParameters.Add(Stratis.SmartContracts.UInt256.Parse(parameterSignature[1]));

                    else
                        throw new MethodParameterStringSerializerException($"Parameter type '{parameterType}' is not supported.");
                }
                catch (Exception e) when (e is FormatException || e is OverflowException || e is ArgumentException || e is ArgumentNullException)
                {
                    throw new MethodParameterStringSerializerException($"Error deserializing parameter {parameterType} with value {parameterValue}", e);
                }
            }


            return processedParameters.ToArray();
        }

        /// <inheritdoc />
        private string EscapeAndJoin(string[] parameters)
        {
            IEnumerable<string> escaped = this.EscapePipesAndHashes(parameters);
            return string.Join('|', escaped);
        }

        /// <summary>
        /// Escapes any pipes and hashes in the method parameters.
        /// </summary>
        private IEnumerable<string> EscapePipesAndHashes(string[] parameter)
        {
            IEnumerable<string> processedPipes = parameter.Select(pipeparam => pipeparam = pipeparam.Replace("|", @"\|"));

            IEnumerable<string> processedHashes = processedPipes.Select(hashparam =>
            {

                // This delegate splits the string by the hash character.
                // 
                // If the split array is longer than 2 then we need to 
                // reconstruct the parameter by escaping all hashes
                // after the first one.
                // 
                // Once this is done, prepend the string with the data type,
                // which is an integer representation of MethodParameterDataType,
                // as well as a hash, so that it can be split again upon deserialization.
                //
                // I.e. 3#dcg#5d# will split into 3 / dcg / 5d
                // and then dcg / fd will be reconstructed to dcg\\#5d\\# and
                // 3# prepended to make 3#dcg\\#5d\\#

                string[] hashes = hashparam.Split('#');
                if (hashes.Length == 2)
                    return hashparam;

                var reconstructed = new List<string>();
                for (int i = 1; i < hashes.Length; i++)
                {
                    reconstructed.Add(hashes[i]);
                }

                string result = string.Join('#', reconstructed).Replace("#", @"\#");
                return hashes[0].Insert(hashes[0].Length, "#" + result);
            });

            return processedHashes;
        }
    }

    public class MethodParameterStringSerializerException : Exception
    {
        public MethodParameterStringSerializerException(string message) : base(message)
        {
        }

        public MethodParameterStringSerializerException(string message, Exception innerException)
        : base(message, innerException)
        {
        }
    }
}