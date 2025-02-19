﻿using BinarySerialization;
using Emgu.CV;
using Emgu.CV.CvEnum;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using UVtools.Core.Extensions;
using UVtools.Core.Layers;
using UVtools.Core.Operations;

namespace UVtools.Core.FileFormats;

public class CTBEncryptedFile : FileFormat
{

    #region Constants 
    public const uint MAGIC_CBT_ENCRYPTED = 0x12FD0107;
    public const ushort REPEATRGB15MASK = 0x20;
    public const ushort RLE16EncodingLimit = 0xFFF;
    public const ushort RLEEncryptedMinimumLength = 512;

    public const uint PERLAYER_SETTINGS_DISALLOW_NO_AA = 7; // 7 (This disallow per layer settings and follow global table only) No AA
    public const uint PERLAYER_SETTINGS_DISALLOW       = 15; // 15 (This disallow per layer settings and follow global table only) with AA
    public const uint PERLAYER_SETTINGS_ALLOW          = 0x5000000F; // 1342177295 (This allow per layer settings)

    private const string CTB_DISCLAIMER = "Layout and record format for the ctb and cbddlp file types are the copyrighted programs or codes of CBD Technology (China) Inc..The Customer or User shall not in any manner reproduce, distribute, modify, decompile, disassemble, decrypt, extract, reverse engineer, lease, assign, or sublicense the said programs or codes.";
    private const ushort CTB_DISCLAIMER_SIZE = 320;

    public const byte HASH_LENGTH = 32;
    public const uint LAYER_XOR_KEY = 0xEFBEADDE;

    public const string Secret0 = "HDgSAB0BEiE/AgpPAhwhM1QAAUwHPT8HTywEGiEjVAoBDwEsJgAKC0wVPDoRTwkDATg3AE9HQhAhNF1VZWYkMHYVHQpMEjI3HQEcGFM7OQBPHwkBOD8AGwoIUyAlER1PCBIhN1QKAQ8BLCYABgACX3U6GwwEH191NRsBHBgBND8aHENMATAlAB0GDwc8ORocQ0weOjgbHwAAGi83AAYAAlM0OBBPAQMdeCURARwJUyU5GAYMBRYmdgAHDhhTJSQRGQoCByZ2GxsHCQEmdhIdAAFTNiQRDhsJUzQ4EE8DCRIxexIAHRsSJzJUHAAABiE/GwEcTBInOQEBC0wHMDUcAQAAHDIvWmU8GQMlOQYbBgIUdSIcBhxMFTw6EU8JAwE4NwBPBh9TNHYHGwocXjc3FwRPChwndkcrTxgWNj4aAAMDFCx2FQELTBU6JFQbBwlTNjkZAhoCGiEvVAAZCQE0OhhBTz8HPDoYQ08NHTF2HQFPDhY9NxgJTwMVdSMHCh0fUyIzVA4DABwidgAATx4WNDJYTxwNBTB2FQELTB40OB0fGgASITNUGwcJUzM/GApPChwndgYKGQUWInpUHQoPHCMzBk8LDQc0dhUBC0wXMCIRDBtMAyc5FgMKAQB1IhtPAg0YMHYNABpMEDogER0KCFMzJBsCTwEaJiIVBAofUzQ4EE8KHgE6JAdBZTwfMDcHCkNMHjQ9EU8WAwYndgcHBgoHdTAGAAJMBz0/B08fHhwxIxcbHEwSOzJUBwoAA3UiHApPXzd1IhEMBwIcOTkTFk8LHHUwGx0YDQExdhUBC0wcJTMaTk8/BiUmGx0bTBwlMxpCHAMGJzURTxwDHyAiHQABH191IhwOG0wENC9UGApMEDQ4VAwdCRIhM1QNChgHMCRUHx0DFyA1ABxPChwndgAHCkwQOjgHGgIJASZ4";
    public const string Secret1 = "hQ36XB6yTk+zO02ysyiowt8yC1buK+nbLWyfY40EXoU=";
    public const string Secret2 = "Wld+ampndVJecmVjYH5cWQ==";
        

    public static readonly string Preamble = CryptExtensions.XORCipherString(System.Convert.FromBase64String(Secret0), About.Software);
    public static byte[] Bigfoot       = CryptExtensions.XORCipher(System.Convert.FromBase64String(Secret1), About.Software);
    public static byte[] CookieMonster = CryptExtensions.XORCipher(System.Convert.FromBase64String(Secret2), About.Software);

    #endregion

    #region Sub Classes

    public class FileHeader
    {
        public const byte TABLE_SIZE = 48;

        [FieldOrder(0)] public uint Magic { get; set; } = MAGIC_CBT_ENCRYPTED;
        [FieldOrder(1)] public uint SettingsSize { get; set; } = SlicerSettings.TABLE_SIZE;
        [FieldOrder(2)] public uint SettingsOffset { get; set; } = TABLE_SIZE;
        [FieldOrder(3)] public uint Unknown1 { get; set; } // set to 0
        [FieldOrder(4)] public uint Unknown2 { get; set; } = 4; // set to 4
        [FieldOrder(5)] public uint SignatureSize { get; set; }
        [FieldOrder(6)] public uint SignatureOffset { get; set; }
        [FieldOrder(7)] public uint Unknown { get; set; } //set to 0
        [FieldOrder(8)] public ushort Unknown4 { get; set; } = 1; // set to 1
        [FieldOrder(9)] public ushort Unknown5 { get; set; } = 1; // set to 1
        [FieldOrder(10)] public uint Unknown6 { get; set; } // set to 0
        [FieldOrder(11)] public uint Unknown7 { get; set; } = 42; // probably 0x2A
        [FieldOrder(12)] public uint Unknown8 { get; set; } // probably 0 or 1

        public override string ToString()
        {
            return $"{nameof(Magic)}: {Magic}, {nameof(SettingsSize)}: {SettingsSize}, {nameof(SettingsOffset)}: {SettingsOffset}, {nameof(Unknown1)}: {Unknown1}, {nameof(Unknown2)}: {Unknown2}, {nameof(SignatureSize)}: {SignatureSize}, {nameof(SignatureOffset)}: {SignatureOffset}, {nameof(Unknown)}: {Unknown}, {nameof(Unknown4)}: {Unknown4}, {nameof(Unknown5)}: {Unknown5}, {nameof(Unknown6)}: {Unknown6}, {nameof(Unknown7)}: {Unknown7}, {nameof(Unknown8)}: {Unknown8}";
        }
    }

    public class SlicerSettings
    {
        public const ushort TABLE_SIZE = 288;

        private string _machineName = DefaultMachineName;

        /// <summary>
        /// Checksum of unix timestamp
        /// </summary>
        [FieldOrder(0)] public ulong ChecksumValue { get; set; } = 0xCAFEBABE;
        [FieldOrder(1)] public uint LayerPointersOffset { get; set; }
        [FieldOrder(2)] public float DisplayWidth { get; set; }
        [FieldOrder(3)] public float DisplayHeight { get; set; }
        [FieldOrder(4)] public float MachineZ { get; set; }
        [FieldOrder(5)] public uint Unknown1 { get; set; }
        [FieldOrder(6)] public uint Unknown2 { get; set; }
        [FieldOrder(7)] public float TotalHeightMillimeter { get; set; }
        [FieldOrder(8)] public float LayerHeight { get; set; }
        [FieldOrder(9)] public float ExposureTime { get; set; }
        [FieldOrder(10)] public float BottomExposureTime { get; set; }
        [FieldOrder(11)] public float LightOffDelay { get; set; }
        [FieldOrder(12)] public uint BottomLayerCount { get; set; }
        [FieldOrder(13)] public uint ResolutionX { get; set; }
        [FieldOrder(14)] public uint ResolutionY { get; set; }
        [FieldOrder(15)] public uint LayerCount { get; set; }
        [FieldOrder(16)] public uint LargePreviewOffset { get; set; }
        [FieldOrder(17)] public uint SmallPreviewOffset { get; set; }
        [FieldOrder(18)] public uint PrintTime { get; set; }
        [FieldOrder(19)] public uint ProjectorType { get; set; }
        [FieldOrder(20)] public float BottomLiftHeight { get; set; }
        [FieldOrder(21)] public float BottomLiftSpeed { get; set; }
        [FieldOrder(22)] public float LiftHeight { get; set; }
        [FieldOrder(23)] public float LiftSpeed { get; set; }
        [FieldOrder(24)] public float RetractSpeed { get; set; }
        [FieldOrder(25)] public float MaterialMilliliters { get; set; }
        [FieldOrder(26)] public float MaterialGrams { get; set; }
        [FieldOrder(27)] public float MaterialCost { get; set; }
        [FieldOrder(28)] public float BottomLightOffDelay { get; set; }
        [FieldOrder(29)] public uint Unknown3 { get; set; } = 1;
        [FieldOrder(30)] public ushort LightPWM { get; set; }
        [FieldOrder(31)] public ushort BottomLightPWM { get; set; }
        [FieldOrder(32)] public uint LayerXorKey { get; set; }
        [FieldOrder(33)] public float BottomLiftHeight2 { get; set; }
        [FieldOrder(34)] public float BottomLiftSpeed2 { get; set; }
        [FieldOrder(35)] public float LiftHeight2 { get; set; }
        [FieldOrder(36)] public float LiftSpeed2 { get; set; }
        [FieldOrder(37)] public float RetractHeight2 { get; set; }
        [FieldOrder(38)] public float RetractSpeed2 { get; set; }
        [FieldOrder(39)] public float RestTimeAfterLift { get; set; }
        [FieldOrder(40)] public uint MachineNameOffset { get; set; }
        [FieldOrder(41)] public uint MachineNameSize { get; set; } = (uint)(string.IsNullOrEmpty(DefaultMachineName) ? 0 : DefaultMachineName.Length);
        [FieldOrder(42)] public uint PerLayerSettings { get; set; } = PERLAYER_SETTINGS_DISALLOW;
        [FieldOrder(43)] public uint Unknown4 { get; set; }
        [FieldOrder(44)] public uint Unknown5 { get; set; } = 8; // Also 1
        [FieldOrder(45)] public float RestTimeAfterRetract { get; set; }
        [FieldOrder(46)] public float RestTimeAfterLift2 { get; set; }
        [FieldOrder(47)] public uint TransitionLayerCount { get; set; }
        [FieldOrder(48)] public float BottomRetractSpeed { get; set; }
        [FieldOrder(49)] public float BottomRetractSpeed2 { get; set; }
        [FieldOrder(50)] public uint Padding1 { get; set; }
        [FieldOrder(51)] public float Four1 { get; set; } = 4; // Same as CTBv4.PrintParametersV4.Four1)
        [FieldOrder(52)] public uint Padding2 { get; set; } 
        [FieldOrder(53)] public float Four2 { get; set; } = 4; // Same as CTBv4.PrintParametersV4.Four2)
        [FieldOrder(54)] public float RestTimeAfterRetract2 { get; set; }
        [FieldOrder(55)] public float RestTimeAfterLift3 { get; set; }
        [FieldOrder(56)] public float RestTimeBeforeLift { get; set; }
        [FieldOrder(57)] public float BottomRetractHeight2 { get; set; }
        [FieldOrder(58)] public uint Unknown6 { get; set; } // Same as CTBv4.PrintParametersV4.Unknown1)
        [FieldOrder(59)] public uint Unknown7 { get; set; } //  Same as  CTBv4.PrintParametersV4.Unknown2)
        [FieldOrder(60)] public uint Unknown8 { get; set; } = 4; // Same as CTBv4.PrintParametersV4.Unknown3)
        [FieldOrder(61)] public uint LastLayerIndex { get; set; }
        [FieldOrder(62)] public uint Padding3 { get; set; }
        [FieldOrder(63)] public uint Padding4 { get; set; }
        [FieldOrder(64)] public uint Padding5 { get; set; }
        [FieldOrder(65)] public uint Padding6 { get; set; }
        [FieldOrder(66)] public uint DisclaimerOffset { get; set; }
        [FieldOrder(67)] public uint DisclaimerSize { get; set; }
        [FieldOrder(68)] public uint Padding7 { get; set; }
        [FieldOrder(69)] public uint Padding8 { get; set; }
        [FieldOrder(70)] public uint Padding9 { get; set; }
        [FieldOrder(71)] public uint Padding10 { get; set; }

        [Ignore]
        public string MachineName
        {
            get => _machineName;
            set
            {
                if (string.IsNullOrEmpty(value)) value = DefaultMachineName;
                _machineName = value;
                MachineNameSize = string.IsNullOrEmpty(_machineName) ? 0 : (uint)_machineName.Length;
            }
        }

        public override string ToString()
        {
            return $"{nameof(ChecksumValue)}: {ChecksumValue}, {nameof(LayerPointersOffset)}: {LayerPointersOffset}, {nameof(DisplayWidth)}: {DisplayWidth}, {nameof(DisplayHeight)}: {DisplayHeight}, {nameof(MachineZ)}: {MachineZ}, {nameof(Unknown1)}: {Unknown1}, {nameof(Unknown2)}: {Unknown2}, {nameof(TotalHeightMillimeter)}: {TotalHeightMillimeter}, {nameof(LayerHeight)}: {LayerHeight}, {nameof(ExposureTime)}: {ExposureTime}, {nameof(BottomExposureTime)}: {BottomExposureTime}, {nameof(LightOffDelay)}: {LightOffDelay}, {nameof(BottomLayerCount)}: {BottomLayerCount}, {nameof(ResolutionX)}: {ResolutionX}, {nameof(ResolutionY)}: {ResolutionY}, {nameof(LayerCount)}: {LayerCount}, {nameof(LargePreviewOffset)}: {LargePreviewOffset}, {nameof(SmallPreviewOffset)}: {SmallPreviewOffset}, {nameof(PrintTime)}: {PrintTime}, {nameof(ProjectorType)}: {ProjectorType}, {nameof(BottomLiftHeight)}: {BottomLiftHeight}, {nameof(BottomLiftSpeed)}: {BottomLiftSpeed}, {nameof(LiftHeight)}: {LiftHeight}, {nameof(LiftSpeed)}: {LiftSpeed}, {nameof(RetractSpeed)}: {RetractSpeed}, {nameof(MaterialMilliliters)}: {MaterialMilliliters}, {nameof(MaterialGrams)}: {MaterialGrams}, {nameof(MaterialCost)}: {MaterialCost}, {nameof(BottomLightOffDelay)}: {BottomLightOffDelay}, {nameof(Unknown3)}: {Unknown3}, {nameof(LightPWM)}: {LightPWM}, {nameof(BottomLightPWM)}: {BottomLightPWM}, {nameof(LayerXorKey)}: {LayerXorKey}, {nameof(BottomLiftHeight2)}: {BottomLiftHeight2}, {nameof(BottomLiftSpeed2)}: {BottomLiftSpeed2}, {nameof(LiftHeight2)}: {LiftHeight2}, {nameof(LiftSpeed2)}: {LiftSpeed2}, {nameof(RetractHeight2)}: {RetractHeight2}, {nameof(RetractSpeed2)}: {RetractSpeed2}, {nameof(RestTimeAfterLift)}: {RestTimeAfterLift}, {nameof(MachineNameOffset)}: {MachineNameOffset}, {nameof(MachineNameSize)}: {MachineNameSize}, {nameof(PerLayerSettings)}: {PerLayerSettings}, {nameof(Unknown4)}: {Unknown4}, {nameof(Unknown5)}: {Unknown5}, {nameof(RestTimeAfterRetract)}: {RestTimeAfterRetract}, {nameof(RestTimeAfterLift2)}: {RestTimeAfterLift2}, {nameof(TransitionLayerCount)}: {TransitionLayerCount}, {nameof(BottomRetractSpeed)}: {BottomRetractSpeed}, {nameof(BottomRetractSpeed2)}: {BottomRetractSpeed2}, {nameof(Padding1)}: {Padding1}, {nameof(Four1)}: {Four1}, {nameof(Padding2)}: {Padding2}, {nameof(Four2)}: {Four2}, {nameof(RestTimeAfterRetract2)}: {RestTimeAfterRetract2}, {nameof(RestTimeAfterLift3)}: {RestTimeAfterLift3}, {nameof(RestTimeBeforeLift)}: {RestTimeBeforeLift}, {nameof(BottomRetractHeight2)}: {BottomRetractHeight2}, {nameof(Unknown6)}: {Unknown6}, {nameof(Unknown7)}: {Unknown7}, {nameof(Unknown8)}: {Unknown8}, {nameof(LastLayerIndex)}: {LastLayerIndex}, {nameof(Padding3)}: {Padding3}, {nameof(Padding4)}: {Padding4}, {nameof(Padding5)}: {Padding5}, {nameof(Padding6)}: {Padding6}, {nameof(DisclaimerOffset)}: {DisclaimerOffset}, {nameof(DisclaimerSize)}: {DisclaimerSize}, {nameof(Padding7)}: {Padding7}, {nameof(Padding8)}: {Padding8}, {nameof(Padding9)}: {Padding9}, {nameof(Padding10)}: {Padding10}, {nameof(MachineName)}: {MachineName}";
        }
    }

    public class LayerPointer
    {
        [FieldOrder(0)] public uint LayerOffset { get; set; }
        [FieldOrder(1)] public uint Padding1 { get; set; } // 0
        [FieldOrder(2)] public uint LayerTableSize { get; set; } = LayerDef.TABLE_SIZE; // always 0x58
        [FieldOrder(3)] public uint Padding2 { get; set; } // 0

        public override string ToString()
        {
            return $"{nameof(LayerOffset)}: {LayerOffset}, {nameof(Padding1)}: {Padding1}, {nameof(LayerTableSize)}: {LayerTableSize}, {nameof(Padding2)}: {Padding2}";
        }

        public LayerPointer()
        {
        }

        public LayerPointer(uint layerOffset)
        {
            LayerOffset = layerOffset;
        }
    }

    public class LayerDef
    {
        public const byte TABLE_SIZE = 88;

        [FieldOrder(0)] public uint TableSize { get; set; } = TABLE_SIZE;
        [FieldOrder(1)] public float PositionZ { get; set; }
        [FieldOrder(2)] public float ExposureTime { get; set; }
        [FieldOrder(3)] public float LightOffDelay { get; set; }
        [FieldOrder(4)] public uint LayerDefOffset { get; set; }
        [FieldOrder(5)] public uint Unknown2 { get; set; }
        [FieldOrder(6)] public uint DataLength { get; set; }
        [FieldOrder(7)] public uint Unknown3 { get; set; }
        [FieldOrder(8)] public uint EncryptedDataOffset { get; set; }
        [FieldOrder(9)] public uint EncryptedDataLength { get; set; }
        [FieldOrder(10)] public float LiftHeight { get; set; }
        [FieldOrder(11)] public float LiftSpeed { get; set; }
        [FieldOrder(12)] public float LiftHeight2 { get; set; }
        [FieldOrder(13)] public float LiftSpeed2 { get; set; }
        [FieldOrder(14)] public float RetractSpeed { get; set; }
        [FieldOrder(15)] public float RetractHeight2 { get; set; }
        [FieldOrder(16)] public float RetractSpeed2 { get; set; }
        [FieldOrder(17)] public float RestTimeBeforeLift { get; set; }
        [FieldOrder(18)] public float RestTimeAfterLift { get; set; }
        [FieldOrder(19)] public float RestTimeAfterRetract { get; set; }
        [FieldOrder(20)] public float LightPWM { get; set; }
        [FieldOrder(21)] public uint Unknown6 { get; set; }

        [Ignore] public CTBEncryptedFile? Parent { get; set; }

        //[FieldOrder(22)] [FieldLength(nameof(DataLength))] public byte[] RLEData { get; set; }
        [Ignore] public byte[]? RLEData { get; set; }

        public LayerDef() { }

        public LayerDef(CTBEncryptedFile parent, Layer layer)
        {
            Parent = parent;
            SetFrom(layer);
        }

        public void SetFrom(Layer layer)
        {
            PositionZ = layer.PositionZ;
            ExposureTime = layer.ExposureTime;
            LightOffDelay = layer.LightOffDelay;
            LiftHeight = layer.LiftHeightTotal;
            LiftSpeed = layer.LiftSpeed;
            LiftHeight2 = layer.LiftHeight2;
            LiftSpeed2 = layer.LiftSpeed2;
            RetractSpeed = layer.RetractSpeed;
            RetractHeight2 = layer.RetractHeight2;
            RetractSpeed2 = layer.RetractSpeed2;
            RestTimeBeforeLift = layer.WaitTimeAfterCure;
            RestTimeAfterLift = layer.WaitTimeAfterLift;
            RestTimeAfterRetract = layer.WaitTimeBeforeCure;
            LightPWM = layer.LightPWM;
        }

        public void CopyTo(Layer layer)
        {
            layer.PositionZ = PositionZ;
            layer.ExposureTime = ExposureTime;
            layer.LightOffDelay = LightOffDelay;
            layer.LiftHeight = LiftHeight - LiftHeight2;
            layer.LiftSpeed = LiftSpeed;
            layer.LiftHeight2 = LiftHeight2;
            layer.LiftSpeed2 = LiftSpeed2;
            layer.RetractSpeed = RetractSpeed;
            layer.RetractHeight2 = RetractHeight2;
            layer.RetractSpeed2 = RetractSpeed2;
            layer.WaitTimeAfterCure = RestTimeBeforeLift;
            layer.WaitTimeAfterLift = RestTimeAfterLift;
            layer.WaitTimeBeforeCure = RestTimeAfterRetract;
            layer.LightPWM = (byte)LightPWM;
        }

        public Mat DecodeImage(uint layerIndex, bool consumeRle = true)
        {
            var mat = EmguExtensions.InitMat(Parent!.Resolution);
            //var span = mat.GetBytePointer();

            if (Parent.Settings.LayerXorKey > 0)
            {
                ChituboxFile.LayerRleCryptBuffer(Parent.Settings.LayerXorKey, layerIndex, RLEData!);
            }

            int pixel = 0;
            for (var n = 0; n < RLEData!.Length; n++)
            {
                byte code = RLEData[n];
                int stride = 1;

                if ((code & 0x80) == 0x80) // It's a run
                {
                    code &= 0x7f; // Get the run length
                    n++;

                    var slen = RLEData[n];

                    if ((slen & 0x80) == 0)
                    {
                        stride = slen;
                    }
                    else if ((slen & 0xc0) == 0x80)
                    {
                        stride = ((slen & 0x3f) << 8) + RLEData[n + 1];
                        n++;
                    }
                    else if ((slen & 0xe0) == 0xc0)
                    {
                        stride = ((slen & 0x1f) << 16) + (RLEData[n + 1] << 8) + RLEData[n + 2];
                        n += 2;
                    }
                    else if ((slen & 0xf0) == 0xe0)
                    {
                        stride = ((slen & 0xf) << 24) + (RLEData[n + 1] << 16) + (RLEData[n + 2] << 8) + RLEData[n + 3];
                        n += 3;
                    }
                    else
                    {
                        mat.Dispose();
                        throw new FileLoadException("Corrupted RLE data");
                    }
                }

                // Bit extend from 7-bit to 8-bit greymap
                if (code != 0)
                {
                    code = (byte)((code << 1) | 1);
                }

                mat.FillSpan(ref pixel, stride, code);

                //if (stride <= 0) continue; // Nothing to do

                /*if (code == 0) // Ignore blacks, spare cycles
                {
                    pixel += stride;
                    continue;
                }*/

                /*for (; stride > 0; stride--)
                {
                    span[pixel] = code;
                    pixel++;
                }*/
            }

            if (consumeRle) RLEData = null;

            return mat;
        }

        public unsafe byte[] EncodeImage(Mat image, uint layerIndex)
        {
            List<byte> rawData = new();
            byte color = byte.MaxValue >> 1;
            uint stride = 0;
            var span = image.GetBytePointer();
            var imageLength = image.GetLength();

            void AddRep()
            {
                if (stride == 0)
                {
                    return;
                }

                if (stride > 1)
                {
                    color |= 0x80;
                }
                rawData.Add(color);

                if (stride <= 1)
                {
                    // no run needed
                    return;
                }

                if (stride <= 0x7f)
                {
                    rawData.Add((byte)stride);
                    return;
                }

                if (stride <= 0x3fff)
                {
                    rawData.Add((byte)((stride >> 8) | 0x80));
                    rawData.Add((byte)stride);
                    return;
                }

                if (stride <= 0x1fffff)
                {
                    rawData.Add((byte)((stride >> 16) | 0xc0));
                    rawData.Add((byte)(stride >> 8));
                    rawData.Add((byte)stride);
                    return;
                }

                if (stride <= 0xfffffff)
                {
                    rawData.Add((byte)((stride >> 24) | 0xe0));
                    rawData.Add((byte)(stride >> 16));
                    rawData.Add((byte)(stride >> 8));
                    rawData.Add((byte)stride);
                }
            }


            for (int pixel = 0; pixel < imageLength; pixel++)
            {
                var grey7 = (byte)(span[pixel] >> 1);

                if (grey7 == color)
                {
                    stride++;
                }
                else
                {
                    AddRep();
                    color = grey7;
                    stride = 1;
                }
            }

            AddRep();

            if (Parent!.Settings.LayerXorKey > 0)
            {
                RLEData = ChituboxFile.LayerRleCrypt(Parent.Settings.LayerXorKey, layerIndex, rawData);
            }
            else
            {
                RLEData = rawData.ToArray();
            }

            DataLength = (uint)RLEData.Length;

            return RLEData;
        }

        public override string ToString()
        {
            return $"{nameof(TableSize)}: {TableSize}, {nameof(PositionZ)}: {PositionZ}, {nameof(ExposureTime)}: {ExposureTime}, {nameof(LightOffDelay)}: {LightOffDelay}, {nameof(LayerDefOffset)}: {LayerDefOffset}, {nameof(Unknown2)}: {Unknown2}, {nameof(DataLength)}: {DataLength}, {nameof(Unknown3)}: {Unknown3}, {nameof(EncryptedDataOffset)}: {EncryptedDataOffset}, {nameof(EncryptedDataLength)}: {EncryptedDataLength}, {nameof(LiftHeight)}: {LiftHeight}, {nameof(LiftSpeed)}: {LiftSpeed}, {nameof(LiftHeight2)}: {LiftHeight2}, {nameof(LiftSpeed2)}: {LiftSpeed2}, {nameof(RetractSpeed)}: {RetractSpeed}, {nameof(RetractHeight2)}: {RetractHeight2}, {nameof(RetractSpeed2)}: {RetractSpeed2}, {nameof(RestTimeBeforeLift)}: {RestTimeBeforeLift}, {nameof(RestTimeAfterLift)}: {RestTimeAfterLift}, {nameof(RestTimeAfterRetract)}: {RestTimeAfterRetract}, {nameof(LightPWM)}: {LightPWM}, {nameof(Unknown6)}: {Unknown6}, {nameof(RLEData)}: {RLEData?.Length}";
        }
    }

    #region Preview
    /// <summary>
    /// The files contain two preview images.
    /// These are shown on the printer display when choosing which file to print, sparing the poor printer from needing to render a 3D image from scratch.
    /// </summary>
    public class Preview
    {
        /// <summary>
        /// Gets the X dimension of the preview image, in pixels. 
        /// </summary>
        [FieldOrder(0)] public uint ResolutionX { get; set; }

        /// <summary>
        /// Gets the Y dimension of the preview image, in pixels. 
        /// </summary>
        [FieldOrder(1)] public uint ResolutionY { get; set; }

        /// <summary>
        /// Gets the image offset of the encoded data blob.
        /// </summary>
        [FieldOrder(2)] public uint ImageOffset { get; set; }

        /// <summary>
        /// Gets the image length in bytes.
        /// </summary>
        [FieldOrder(3)] public uint ImageLength { get; set; }


        public unsafe Mat Decode(byte[] rawImageData)
        {
            var image = new Mat(new Size((int)ResolutionX, (int)ResolutionY), DepthType.Cv8U, 3);
            var span = image.GetBytePointer();

            int pixel = 0;
            for (int n = 0; n < rawImageData.Length; n++)
            {
                uint dot = (uint)(rawImageData[n] & 0xFF | ((rawImageData[++n] & 0xFF) << 8));
                byte red = (byte)(((dot >> 11) & 0x1F) << 3);
                byte green = (byte)(((dot >> 6) & 0x1F) << 3);
                byte blue = (byte)((dot & 0x1F) << 3);
                int repeat = 1;
                if ((dot & 0x0020) == 0x0020)
                {
                    repeat += rawImageData[++n] & 0xFF | ((rawImageData[++n] & 0x0F) << 8);
                }

                for (int j = 0; j < repeat; j++)
                {
                    span[pixel++] = blue;
                    span[pixel++] = green;
                    span[pixel++] = red;
                }
            }

            return image;
        }

        public override string ToString()
        {
            return $"{nameof(ResolutionX)}: {ResolutionX}, {nameof(ResolutionY)}: {ResolutionY}, {nameof(ImageOffset)}: {ImageOffset}, {nameof(ImageLength)}: {ImageLength}";
        }

        public unsafe byte[] Encode(Mat image)
        {
            List<byte> rawData = new();
            ushort color15 = 0;
            uint rep = 0;

            var span = image.GetBytePointer();
            var imageLength = image.GetLength();

            void RleRGB15()
            {
                switch (rep)
                {
                    case 0:
                        return;
                    case 1:
                        rawData.Add((byte)(color15 & ~REPEATRGB15MASK));
                        rawData.Add((byte)((color15 & ~REPEATRGB15MASK) >> 8));
                        break;
                    case 2:
                        for (int i = 0; i < 2; i++)
                        {
                            rawData.Add((byte)(color15 & ~REPEATRGB15MASK));
                            rawData.Add((byte)((color15 & ~REPEATRGB15MASK) >> 8));
                        }

                        break;
                    default:
                        rawData.Add((byte)(color15 | REPEATRGB15MASK));
                        rawData.Add((byte)((color15 | REPEATRGB15MASK) >> 8));
                        rawData.Add((byte)((rep - 1) | 0x3000));
                        rawData.Add((byte)(((rep - 1) | 0x3000) >> 8));
                        break;
                }
            }

            int pixel = 0;
            while (pixel < imageLength)
            {
                var ncolor15 =
                    // bgr
                    (span[pixel++] >> 3) | ((span[pixel++] >> 2) << 5) | ((span[pixel++] >> 3) << 11);

                if (ncolor15 == color15)
                {
                    rep++;
                    if (rep == RLE16EncodingLimit)
                    {
                        RleRGB15();
                        rep = 0;
                    }
                }
                else
                {
                    RleRGB15();
                    color15 = (ushort)ncolor15;
                    rep = 1;
                }
            }

            RleRGB15();

            ImageLength = (uint)rawData.Count;

            return rawData.ToArray();
        }
    }
    #endregion

    #endregion

    #region Properties
    public override FileFormatType FileType => FileFormatType.Binary;

    public override FileExtension[] FileExtensions { get; } = {
        new(typeof(CTBEncryptedFile), "ctb",           "Chitubox CTB (Encrypted)"),
        new(typeof(CTBEncryptedFile), "encrypted.ctb", "Chitubox CTB (Encrypted)", false, false),
    };

    public override Size[]? ThumbnailsOriginalSize { get; } =
    {
        new(400, 300),
        new(200, 125)
    };

    public Preview[] Previews { get; protected internal set; }

    public FileHeader Header { get; protected internal set; } = new();

    public SlicerSettings Settings { get; protected internal set; } = new();
    public LayerPointer[]? LayersPointer { get; protected internal set; }
    public LayerDef[]? LayersDefinition { get; protected internal set; }

    public override PrintParameterModifier[]? PrintParameterModifiers { get; } = {
        PrintParameterModifier.BottomLayerCount,
        PrintParameterModifier.TransitionLayerCount,

        PrintParameterModifier.BottomLightOffDelay,
        PrintParameterModifier.LightOffDelay,

        PrintParameterModifier.BottomWaitTimeBeforeCure,
        PrintParameterModifier.WaitTimeBeforeCure,

        PrintParameterModifier.BottomExposureTime,
        PrintParameterModifier.ExposureTime,

        PrintParameterModifier.BottomWaitTimeAfterCure,
        PrintParameterModifier.WaitTimeAfterCure,

        PrintParameterModifier.BottomLiftHeight,
        PrintParameterModifier.BottomLiftSpeed,
        PrintParameterModifier.LiftHeight,
        PrintParameterModifier.LiftSpeed,
        PrintParameterModifier.BottomLiftHeight2,
        PrintParameterModifier.BottomLiftSpeed2,
        PrintParameterModifier.LiftHeight2,
        PrintParameterModifier.LiftSpeed2,

        PrintParameterModifier.BottomWaitTimeAfterLift,
        PrintParameterModifier.WaitTimeAfterLift,

        PrintParameterModifier.BottomRetractSpeed,
        PrintParameterModifier.RetractSpeed,
        PrintParameterModifier.BottomRetractHeight2,
        PrintParameterModifier.BottomRetractSpeed2,
        PrintParameterModifier.RetractHeight2,
        PrintParameterModifier.RetractSpeed2,

        PrintParameterModifier.BottomLightPWM,
        PrintParameterModifier.LightPWM
    };
        
    public override PrintParameterModifier[]? PrintParameterPerLayerModifiers { get; } = {
        PrintParameterModifier.PositionZ,
        PrintParameterModifier.LightOffDelay,
        PrintParameterModifier.WaitTimeBeforeCure,
        PrintParameterModifier.ExposureTime,
        PrintParameterModifier.WaitTimeAfterCure,
        PrintParameterModifier.LiftHeight,
        PrintParameterModifier.LiftSpeed,
        PrintParameterModifier.LiftHeight2,
        PrintParameterModifier.LiftSpeed2,
        PrintParameterModifier.WaitTimeAfterLift,
        PrintParameterModifier.RetractSpeed,
        PrintParameterModifier.RetractHeight2,
        PrintParameterModifier.RetractSpeed2,
        PrintParameterModifier.LightPWM
    };

    public override uint ResolutionX
    {
        get => Settings.ResolutionX;
        set
        {
            Settings.ResolutionX = value;
            RaisePropertyChanged();
        }
    }

    public override uint ResolutionY
    {
        get => Settings.ResolutionY;
        set
        {
            Settings.ResolutionY = value;
            RaisePropertyChanged();
        }
    }

    public override float LayerHeight
    {
        get => Settings.LayerHeight;
        set
        {
            Settings.LayerHeight = value;
            RaisePropertyChanged();
        }
    }

    public override byte AntiAliasing { get => 8; set { } }

    public override float DisplayWidth
    {
        get => Settings.DisplayWidth;
        set
        {
            Settings.DisplayWidth = (float)Math.Round(value, 2);
            RaisePropertyChanged();
        }
    }

    public override float DisplayHeight
    {
        get => Settings.DisplayHeight;
        set
        {
            Settings.DisplayHeight = (float)Math.Round(value, 2);
            RaisePropertyChanged();
        }
    }

    public override float MachineZ
    {
        get => Settings.MachineZ > 0 ? Settings.MachineZ : base.MachineZ;
        set => base.MachineZ = Settings.MachineZ = (float)Math.Round(value, 2);
    }

    public override FlipDirection DisplayMirror
    {
        get => Settings.ProjectorType == 0 ? FlipDirection.None : FlipDirection.Horizontally;
        set
        {
            Settings.ProjectorType = value == FlipDirection.None ? 0u : 1;
            RaisePropertyChanged();
        }
    }

    /* TODO: Find AntiAliasLevel in file */
    /*
    public override byte AntiAliasing
    {
        get => (byte)(Settings.AntiAliasLevel);
        set
        {
            Settings.AntiAliasLevel = value;
            RaisePropertyChanged();
        }
    }*/

    public override float PrintHeight
    {
        get => base.PrintHeight;
        set => base.PrintHeight = Settings.TotalHeightMillimeter = base.PrintHeight;
    }

    public override uint LayerCount
    {
        get => base.LayerCount;
        set
        {
            base.LayerCount = Settings.LayerCount = base.LayerCount;
            Settings.LastLayerIndex = LastLayerIndex;
        }
    }

    public override ushort BottomLayerCount
    {
        get => (ushort)Settings.BottomLayerCount;
        set => base.BottomLayerCount = (ushort)(Settings.BottomLayerCount = value);
    }

    public override TransitionLayerTypes TransitionLayerType => TransitionLayerTypes.Software;

    public override ushort TransitionLayerCount
    {
        get => (ushort)Settings.TransitionLayerCount;
        set => base.TransitionLayerCount = (ushort)(Settings.TransitionLayerCount = Math.Min(value, MaximumPossibleTransitionLayerCount));
    }

    public override float BottomLightOffDelay
    {
        get => Settings.BottomLightOffDelay;
        set
        {
            base.BottomLightOffDelay = Settings.BottomLightOffDelay = (float)Math.Round(value, 2);
            if (value > 0)
            {
                WaitTimeBeforeCure = 0;
                WaitTimeAfterCure = 0;
                WaitTimeAfterLift = 0;
            }
        }
    }

    public override float LightOffDelay
    {
        get => Settings.LightOffDelay;
        set
        {
            base.LightOffDelay = Settings.LightOffDelay = (float)Math.Round(value, 2);
            if (value > 0)
            {
                WaitTimeBeforeCure = 0;
                WaitTimeAfterCure = 0;
                WaitTimeAfterLift = 0;
            }
        }
    }

    public override float BottomWaitTimeBeforeCure
    {
        get => base.BottomWaitTimeBeforeCure > 0 ? base.BottomWaitTimeBeforeCure : FirstLayer?.WaitTimeBeforeCure ?? 0;
        set => base.BottomWaitTimeBeforeCure = value;
    }


    public override float WaitTimeBeforeCure
    {
        get => Settings.RestTimeAfterRetract;
        set
        {
            base.WaitTimeBeforeCure = Settings.RestTimeAfterRetract = Settings.RestTimeAfterRetract2 = (float)Math.Round(value, 2);
            if (value > 0)
            {
                BottomLightOffDelay = 0;
                LightOffDelay = 0;
            }
        }
    }

    public override float BottomExposureTime
    {
        get => Settings.BottomExposureTime;
        set => base.BottomExposureTime = Settings.BottomExposureTime = (float)Math.Round(value, 2);
    }

    public override float BottomWaitTimeAfterCure
    {
        get => base.BottomWaitTimeAfterCure > 0 ? base.BottomWaitTimeAfterCure : FirstLayer?.WaitTimeAfterCure ?? 0;
        set => base.BottomWaitTimeAfterCure = value;
    }

    public override float WaitTimeAfterCure
    {
        get => Settings.RestTimeBeforeLift;
        set
        {
            base.WaitTimeAfterCure = Settings.RestTimeBeforeLift = (float)Math.Round(value, 2);
            if (value > 0)
            {
                BottomLightOffDelay = 0;
                LightOffDelay = 0;
            }
        }
    }

    public override float ExposureTime
    {
        get => Settings.ExposureTime;
        set => base.ExposureTime = Settings.ExposureTime = (float)Math.Round(value, 2);
    }

    public override float BottomLiftHeight
    {
        get => Math.Max(0,Settings.BottomLiftHeight - Settings.BottomLiftHeight2);
        set
        {
            value = (float)Math.Round(value, 2);
            Settings.BottomLiftHeight = (float)Math.Round(value + Settings.BottomLiftHeight2, 2);
            base.BottomLiftHeight = value;
        }
    }

    public override float BottomLiftSpeed
    {
        get => Settings.BottomLiftSpeed;
        set => base.BottomLiftSpeed = Settings.BottomLiftSpeed = (float)Math.Round(value, 2);
    }

    public override float LiftHeight
    {
        get => Math.Max(0,Settings.LiftHeight - Settings.LiftHeight2);
        set
        {
            value = (float)Math.Round(value, 2);
            Settings.LiftHeight = (float)Math.Round(value + Settings.LiftHeight2, 2);
            base.LiftHeight = value;
        }
    }
        
    public override float LiftSpeed
    {
        get => Settings.LiftSpeed;
        set => base.LiftSpeed = Settings.LiftSpeed = (float)Math.Round(value, 2);
    }

    public override float BottomLiftHeight2
    {
        get => Settings.BottomLiftHeight2;
        set
        {
            var bottomLiftHeight = BottomLiftHeight;
            Settings.BottomLiftHeight2 = (float)Math.Round(value, 2);
            BottomLiftHeight = bottomLiftHeight;
            base.BottomLiftHeight2 = Settings.BottomLiftHeight2; 
        }
    }

    public override float BottomLiftSpeed2
    {
        get => Settings.BottomLiftSpeed2;
        set => base.BottomLiftSpeed2 = Settings.BottomLiftSpeed2 = (float)Math.Round(value, 2);
    }

    public override float LiftHeight2
    {
        get => Settings.LiftHeight2;
        set
        {
            var liftHeight = LiftHeight;
            Settings.LiftHeight2 = (float)Math.Round(value, 2);
            LiftHeight = liftHeight;
            base.LiftHeight2 = Settings.LiftHeight2;
        }
    }
        
    public override float LiftSpeed2
    {
        get => Settings.LiftSpeed2;
        set => base.LiftSpeed2 = Settings.LiftSpeed2 = (float)Math.Round(value, 2);
    }

    public override float BottomWaitTimeAfterLift
    {
        get => base.BottomWaitTimeAfterLift > 0 ? base.BottomWaitTimeAfterLift : FirstLayer?.WaitTimeAfterLift ?? 0;
        set => base.BottomWaitTimeAfterLift = value;
    }

    public override float WaitTimeAfterLift
    {
        get => Settings.RestTimeAfterLift;
        set
        {
            base.WaitTimeAfterLift = Settings.RestTimeAfterLift = Settings.RestTimeAfterLift2 = Settings.RestTimeAfterLift3 = (float)Math.Round(value, 2);
            if (value > 0)
            {
                BottomLightOffDelay = 0;
                LightOffDelay = 0;
            }
        }
    }

    public override float BottomRetractSpeed
    {
        get => Settings.BottomRetractSpeed;
        set => base.BottomRetractSpeed = Settings.BottomRetractSpeed = (float)Math.Round(value, 2);
    }

    public override float RetractSpeed
    {
        get => Settings.RetractSpeed;
        set => base.RetractSpeed = Settings.RetractSpeed = (float)Math.Round(value, 2);
    }

    public override float BottomRetractHeight2
    {
        get => Settings.BottomRetractHeight2;
        set
        {
            value = Math.Clamp((float)Math.Round(value, 2), 0, RetractHeightTotal);
            base.BottomRetractHeight2 = Settings.BottomRetractHeight2 = value;
        }
    }

    public override float BottomRetractSpeed2
    {
        get => Settings.BottomRetractSpeed2;
        set => base.BottomRetractSpeed2 = Settings.BottomRetractSpeed2 = (float)Math.Round(value, 2);
    }

    public override float RetractHeight2
    {
        get => Settings.RetractHeight2;
        set
        {
            value = Math.Clamp((float)Math.Round(value, 2), 0, RetractHeightTotal);
            base.RetractHeight2 = Settings.RetractHeight2 = value;
        }
    }

    public override float RetractSpeed2
    {
        get => Settings.RetractSpeed2;
        set => base.RetractSpeed2 = Settings.RetractSpeed2 = (float)Math.Round(value, 2);
    }

    public override byte BottomLightPWM
    {
        get => (byte)Settings.BottomLightPWM;
        set => base.BottomLightPWM = (byte)(Settings.BottomLightPWM = value);
    }

    public override byte LightPWM
    {
        get => (byte)Settings.LightPWM;
        set => base.LightPWM = (byte)(Settings.LightPWM = value);
    }

    public override float PrintTime
    {
        get => base.PrintTime;
        set
        {
            base.PrintTime = value;
            Settings.PrintTime = (uint)base.PrintTime;
        }
    }

    public override string MachineName
    {
        get => Settings.MachineName;
        set => base.MachineName = Settings.MachineName = value;
    }

    public override float MaterialMilliliters
    {
        get => base.MaterialMilliliters;
        set
        {
            base.MaterialMilliliters = value;
            Settings.MaterialMilliliters = base.MaterialMilliliters;
        }
    }

    public override float MaterialGrams
    {
        get => Settings.MaterialGrams;
        set => base.MaterialGrams = Settings.MaterialGrams = (float)Math.Round(value, 3);
    }

    public override float MaterialCost
    {
        get => (float)Math.Round(Settings.MaterialCost, 3);
        set => base.MaterialCost = Settings.MaterialCost = (float)Math.Round(value, 3);
    }

    public override object[] Configs
    {
        get
        {
            return new object[] { Settings };
        }
    }



    #endregion

    #region Constructors
    public CTBEncryptedFile()
    {
        Previews = new Preview[ThumbnailsCount];

        /*if (Bigfoot is not null && Bigfoot[0] == 0 && File.Exists("MAGIC.ectb"))
        {
            using var fs = new FileStream("MAGIC.ectb", FileMode.Open);
            fs.ReadBytes(Bigfoot);
            fs.ReadBytes(CookieMonster);
        }*/
    }
    #endregion

    #region Methods

    public override void Clear()
    {
        base.Clear();

        for (byte i = 0; i < ThumbnailsCount; i++)
        {
            Previews[i] = new Preview();
        }
    }

    public override bool CanProcess(string? fileFullPath)
    {
        if (!base.CanProcess(fileFullPath)) return false;

        try
        {
            using var fs = new BinaryReader(new FileStream(fileFullPath!, FileMode.Open, FileAccess.Read));
            var magic = fs.ReadUInt32();
            return magic is MAGIC_CBT_ENCRYPTED;
        }
        catch (Exception e)
        {
            Debug.WriteLine(e);
        }

        return false;
    }

    private void SanitizeProperties()
    {
        Settings.PerLayerSettings = AllLayersAreUsingGlobalParameters
            ? PERLAYER_SETTINGS_DISALLOW
            : PERLAYER_SETTINGS_ALLOW;
    }


    protected override void DecodeInternally(OperationProgress progress)
    {
        using var inputFile = new FileStream(FileFullPath!, FileMode.Open, FileAccess.Read);
        Header = Helpers.Deserialize<FileHeader>(inputFile);
        Debug.WriteLine($"Header: {Header}");

        if (Header.Magic is not MAGIC_CBT_ENCRYPTED)
        {
            throw new FileLoadException($"Not a valid CTB encrypted file! Magic Value: {Header.Magic}", FileFullPath);
        }

        inputFile.Seek(Header.SettingsOffset, SeekOrigin.Begin);
            
        var encryptedBlock = inputFile.ReadBytes(Header.SettingsSize); 
        using (var ms = CryptExtensions.AesCryptMemoryStream(encryptedBlock, Bigfoot, CipherMode.CBC, PaddingMode.None, false, CookieMonster))
        {
            Settings = Helpers.Deserialize<SlicerSettings>(ms);
        }

        Debug.WriteLine($"Settings: {Settings}");

        /* validate hash */
        var checksumBytes = BitExtensions.ToBytesLittleEndian(Settings.ChecksumValue);
        var checksumHash = CryptExtensions.ComputeSHA256Hash(checksumBytes);
        var encryptedHash = CryptExtensions.AesCryptBytes(checksumHash, Bigfoot, CipherMode.CBC, PaddingMode.None, true, CookieMonster);

        inputFile.Seek(-HASH_LENGTH, SeekOrigin.End);
        var hash = inputFile.ReadBytes(HASH_LENGTH);
        if (!hash.SequenceEqual(encryptedHash))
        {
            throw new FileLoadException("The file checksum does not match, malformed file.", FileFullPath);
        }

        progress.Reset(OperationProgress.StatusDecodePreviews, ThumbnailsCount);

        if (Thumbnails is not null)
        {
            for (byte i = 0; i < ThumbnailsCount; i++)
            {
                uint offsetAddress = i == 0
                    ? Settings.LargePreviewOffset
                    : Settings.SmallPreviewOffset;
                if (offsetAddress == 0) continue;

                inputFile.Seek(offsetAddress, SeekOrigin.Begin);
                Previews[i] = Helpers.Deserialize<Preview>(inputFile);

                Debug.Write($"Preview {i} -> ");
                Debug.WriteLine(Previews[i]);

                inputFile.Seek(Previews[i].ImageOffset, SeekOrigin.Begin);
                byte[] rawImageData = new byte[Previews[i].ImageLength];
                inputFile.Read(rawImageData, 0, (int) Previews[i].ImageLength);

                Thumbnails[i] = Previews[i].Decode(rawImageData);
                progress++;
            }
        }

        /* Read the settings and disclaimer */
        inputFile.Seek(Settings.MachineNameOffset, SeekOrigin.Begin);
        var machineNameBytes = inputFile.ReadBytes(Settings.MachineNameSize);
        Settings.MachineName = Encoding.UTF8.GetString(machineNameBytes);

        /* TODO: read the disclaimer here? we can really just ignore it though...*/

        /* start gathering up the layers */
        progress.Reset(OperationProgress.StatusGatherLayers, Settings.LayerCount);
        inputFile.Seek(Settings.LayerPointersOffset, SeekOrigin.Begin);

        LayersPointer = new LayerPointer[Settings.LayerCount];
        for (uint layerIndex = 0; layerIndex < Settings.LayerCount; layerIndex++)
        {
            progress.ThrowIfCancellationRequested();
            LayersPointer[layerIndex] = Helpers.Deserialize<LayerPointer>(inputFile);
            Debug.WriteLine($"pointer[{layerIndex}]: {LayersPointer[layerIndex]}");
            progress++;
        }

        progress.Reset(OperationProgress.StatusDecodeLayers, Settings.LayerCount);
        Init(Settings.LayerCount, DecodeType == FileDecodeType.Partial);
        LayersDefinition = new LayerDef[LayerCount];
        var buggyLayers = new ConcurrentBag<uint>();

        foreach (var batch in BatchLayersIndexes())
        {
            foreach (var layerIndex in batch)
            {
                progress.ThrowIfCancellationRequested();

                inputFile.Seek(LayersPointer[layerIndex].LayerOffset, SeekOrigin.Begin);
                LayersDefinition[layerIndex] = Helpers.Deserialize<LayerDef>(inputFile);
                LayersDefinition[layerIndex].Parent = this;
                if (DecodeType == FileDecodeType.Full) LayersDefinition[layerIndex].RLEData = inputFile.ReadBytes(LayersDefinition[layerIndex].DataLength);
                Debug.WriteLine($"layer[{layerIndex}]: {LayersDefinition[layerIndex]}");
            }

            if (DecodeType == FileDecodeType.Full)
            {
                Parallel.ForEach(batch, CoreSettings.GetParallelOptions(progress), layerIndex =>
                {
                    var layerDef = LayersDefinition[layerIndex];


                    if (layerDef.EncryptedDataLength > 0)
                    {
                        /* Decrypt RLE data here */

                        var byteBuffer = new byte[layerDef.EncryptedDataLength];
                        Array.Copy(layerDef.RLEData!, (int)layerDef.EncryptedDataOffset, byteBuffer, 0,
                            (int)layerDef.EncryptedDataLength);

                        byteBuffer = CryptExtensions.AesCryptBytes(byteBuffer, Bigfoot, CipherMode.CBC,
                            PaddingMode.None, false, CookieMonster);
                        Array.Copy(byteBuffer, 0, layerDef.RLEData!, layerDef.EncryptedDataOffset,
                            layerDef.EncryptedDataLength);
                    }

                    bool isBugged = false;
                    /* bug fix for Chitubox when a small layer RLE data is encrypted */
                    if (layerDef.EncryptedDataLength > 0 && layerDef.RLEData!.Length < RLEEncryptedMinimumLength &&
                        layerDef.RLEData.Length % 16 != 0)
                    {
                        buggyLayers.Add((uint)layerIndex);
                        isBugged = true;
                        layerDef.RLEData = null; /* clean up RLE data */
                    }

                    if (isBugged)
                    {
                        _layers[layerIndex] = new Layer((uint)layerIndex, this);
                    }
                    else
                    {
                        _layers[layerIndex] = new Layer((uint) layerIndex, layerDef.DecodeImage((uint) layerIndex), this);
                    }

                    progress.LockAndIncrement();
                });
            }
        }

        if (buggyLayers.Count == LayerCount)
        {
            throw new FileLoadException("Unable to load this file due to Chitubox bug affecting every layer." +
                                        "Please increase the portion of the plate in use and reslice the file.");
        }

        for (uint layerIndex = 0; layerIndex < LayerCount; layerIndex++)
        {
            LayersDefinition[layerIndex].CopyTo(this[layerIndex]);
        }

        if (!buggyLayers.IsEmpty)
        {
            var sortedLayerIndexes = buggyLayers.ToArray();
            Array.Sort(sortedLayerIndexes);
            uint correctedLayerCount = 0;

            foreach (var layerIndex in sortedLayerIndexes)
            {
                int direction = layerIndex == 0 ? 1 : -1;

                /* clone from the next one that has a mat */
                for (int layerIndexForClone = (int)(layerIndex + direction); 
                     layerIndexForClone >= 0 && layerIndexForClone < LayerCount;
                     layerIndexForClone += direction)
                {
                    if (!this[layerIndexForClone].HaveImage) continue;
                    this[layerIndexForClone].CopyImageTo(this[layerIndex]);
                    correctedLayerCount++;
                        
                    /* TODO: Report to the user that a layer was cloned to work around chitubox crypto bug */
                        
                    break;
                }
            }

            if (correctedLayerCount < buggyLayers.Count)
            {
                throw new FileLoadException(
                    "Unable to load this file due to an Chitubox bug and the impossibility to auto correct some of these layers.\n" +
                    "Please increase the portion of the plate in use and re-slice the file.");
            }
        }
        //inputFile.ReadBytes(HashLength);
    }

    protected override void EncodeInternally(OperationProgress progress)
    {
        using var outputFile = new FileStream(TemporaryOutputFileFullPath, FileMode.Create, FileAccess.Write);
        //uint currentOffset = 0;
        /* Create the file header and fill out what we can. SignatureOffset will have to be populated later
         * this will be the last thing written to file */
        Header.SettingsSize = (uint)Helpers.Serializer.SizeOf(Settings);
        Header.SettingsOffset = (uint)Helpers.Serializer.SizeOf(Header);

        SanitizeProperties();

        if (Settings.LayerXorKey == 0)
        {
            Settings.LayerXorKey = LAYER_XOR_KEY;
        }

        outputFile.Seek(Header.SettingsOffset + Header.SettingsSize, SeekOrigin.Begin);

        progress.Reset(OperationProgress.StatusEncodePreviews, 2);

        Mat?[] thumbnails = { GetThumbnail(true), GetThumbnail(false) };
        for (byte i = 0; i < thumbnails.Length; i++)
        {
            var image = thumbnails[i];
            if (image is null) continue;

            Preview preview = new()
            {
                ResolutionX = (uint)image.Width,
                ResolutionY = (uint)image.Height,
            };

            var previewBytes = preview.Encode(image);

            if (previewBytes.Length == 0) continue;

            if (i == 0)
            {
                Settings.LargePreviewOffset = (uint)outputFile.Position;
            }
            else
            {
                Settings.SmallPreviewOffset = (uint)outputFile.Position;
            }

            preview.ImageOffset = (uint)(outputFile.Position + Helpers.Serializer.SizeOf(preview));

            Helpers.SerializeWriteFileStream(outputFile, preview);
            outputFile.WriteBytes(previewBytes);
            progress++;
        }

        Settings.MachineNameOffset = (uint)outputFile.Position;
        Settings.MachineNameSize = (uint)Settings.MachineName.Length;
        var machineNameBytes = Encoding.UTF8.GetBytes(Settings.MachineName);

        outputFile.WriteBytes(machineNameBytes);

        Settings.DisclaimerOffset = (uint)outputFile.Position;
        Settings.DisclaimerSize = CTB_DISCLAIMER_SIZE;
        outputFile.WriteBytes(Encoding.UTF8.GetBytes(CTB_DISCLAIMER));

        Settings.LayerPointersOffset = (uint)outputFile.Position;

        /* we'll write this after we write out all of the layers ... */
        LayersPointer = new LayerPointer[LayerCount];
        LayersDefinition = new LayerDef[LayerCount];

        uint layerTableSize = (uint)Helpers.Serializer.SizeOf(new LayerPointer()) * LayerCount;

        outputFile.Seek(outputFile.Position + layerTableSize, SeekOrigin.Begin);

        progress.Reset(OperationProgress.StatusEncodeLayers, LayerCount);
        Parallel.For(0, LayerCount, CoreSettings.GetParallelOptions(progress), layerIndex =>
        {
            var layerDef = new LayerDef(this, this[layerIndex]);
            using (var mat = this[layerIndex].LayerMat)
            {
                layerDef.EncodeImage(mat!, (uint)layerIndex);
                LayersDefinition[layerIndex] = layerDef;
            }

            progress.LockAndIncrement();
        });

        progress.Reset(OperationProgress.StatusWritingFile, LayerCount);
        for (uint layerIndex = 0; layerIndex < LayerCount; layerIndex++)
        {
            progress.ThrowIfCancellationRequested();
            var layerDef = LayersDefinition[layerIndex];
            LayersPointer[layerIndex] = new LayerPointer((uint)outputFile.Position);

            layerDef.LayerDefOffset = LayersPointer[layerIndex].LayerOffset + LayerDef.TABLE_SIZE;
            Helpers.SerializeWriteFileStream(outputFile, layerDef);
            outputFile.WriteBytes(layerDef.RLEData!);
            progress++;
        }
            
        /* write the final hash */
        var hash = CryptExtensions.ComputeSHA256Hash(BitExtensions.ToBytesLittleEndian(Settings.ChecksumValue));
        var encryptedHash = CryptExtensions.AesCryptBytes(hash, Bigfoot, CipherMode.CBC, PaddingMode.None, true, CookieMonster);
        Header.SignatureOffset = (uint)outputFile.Position;
        Header.SignatureSize = (uint)encryptedHash.Length;
        outputFile.WriteBytes(encryptedHash);

        // Rewind

        // Layer pointers
        outputFile.Seek(Settings.LayerPointersOffset, SeekOrigin.Begin);
        for (uint layerIndex = 0; layerIndex < LayersPointer.Length; layerIndex++)
        {
            Helpers.SerializeWriteFileStream(outputFile, LayersPointer[layerIndex]);
        }

        // Settings
        outputFile.Seek(Header.SettingsOffset, SeekOrigin.Begin);
        var settingsBytes = Helpers.Serialize(Settings).ToArray();
        var encryptedSettings = CryptExtensions.AesCryptBytes(settingsBytes, Bigfoot, CipherMode.CBC, PaddingMode.None, true, CookieMonster);
        outputFile.WriteBytes(encryptedSettings);

        // Header
        outputFile.Seek(0, SeekOrigin.Begin);
        Helpers.SerializeWriteFileStream(outputFile, Header);
    }

    protected override void PartialSaveInternally(OperationProgress progress)
    {
        SanitizeProperties();

        using var outputFile = new FileStream(TemporaryOutputFileFullPath, FileMode.Open, FileAccess.Write);
            
        outputFile.Seek(Header.SettingsOffset, SeekOrigin.Begin);
        var settingsBytes = Helpers.Serialize(Settings).ToArray();
        var encryptedSettings = CryptExtensions.AesCryptBytes(settingsBytes, Bigfoot, CipherMode.CBC, PaddingMode.None, true, CookieMonster);
        outputFile.WriteBytes(encryptedSettings);

        for (uint layerIndex = 0; layerIndex < LayersPointer!.Length; layerIndex++)
        {
            LayersDefinition![layerIndex].SetFrom(this[layerIndex]);
            outputFile.Seek(LayersPointer[layerIndex].LayerOffset, SeekOrigin.Begin);
            Helpers.SerializeWriteFileStream(outputFile, LayersDefinition[layerIndex]);
        }
    }
    #endregion

    #region Static Methods
    public static void CryptFile(string filePath)
    {
        using var msReader = new MemoryStream(File.ReadAllBytes(filePath));
        using var msWriter = new MemoryStream();
        msReader.CopyTo(msWriter);
        msWriter.Position = 0;
        msReader.Position = 0;
        var writer = new BinaryWriter(msWriter);
        var reader = new BinaryReader(msReader);

        /* magic */
        var magic = reader.ReadUInt32();
        if (magic != MAGIC_CBT_ENCRYPTED)
        {
            Console.Write("File does not appear to be an encrypted CTB. Aborting.");
            return;
        }

        writer.Write(magic);
        var headerLength = reader.ReadUInt32();
        writer.Write(headerLength);
        var headerOffset = reader.ReadUInt32();
        writer.Write(headerOffset);

        var currentPos = msReader.Position;
        msReader.Seek(headerOffset + 24, SeekOrigin.Begin); // Paddings of 0


        var encrypt = reader.ReadUInt32() == 0 && reader.ReadUInt32() == 0; // Both paddings must be zero so we know we need to encrypt!
        msReader.Seek(currentPos, SeekOrigin.Begin);

        Console.Write($"Crypt mode: {(encrypt ? "Encrypting" : "Decrypting")}");

        /* pass through rest of data until encrypted header */
        var bytesToPassthru = headerOffset - msReader.Position;
        var temp = reader.ReadBytes((int)bytesToPassthru);
        writer.Write(temp);
        uint printerNameLength = 0;

        var originalHeader = reader.ReadBytes((int)headerLength);

        if (encrypt)
        {
            printerNameLength = BitExtensions.ToUIntLittleEndian(originalHeader, 164);
        }

        /* decrypt header with recovered keys */
        var cryptedData = CryptExtensions.AesCryptBytes(originalHeader, Bigfoot, CipherMode.CBC, PaddingMode.None, encrypt, CookieMonster);
        writer.Write(cryptedData);

        /* get machine name length */
        if (!encrypt)
        {
            printerNameLength = BitExtensions.ToUIntLittleEndian(cryptedData, 164);
        }

        /* pass through the next 2 dwords */
        writer.Write(reader.ReadUInt32());
        writer.Write(reader.ReadUInt32());

        /* get offset and length of next section (not encrypted) */
        var nextOffset = reader.ReadUInt32();
        var nextLength = reader.ReadUInt32();

        writer.Write(nextOffset);
        writer.Write(nextLength);

        /* how many bytes from current position till the next offset */
        bytesToPassthru = nextOffset - msReader.Position;
        writer.Write(reader.ReadBytes((int)bytesToPassthru));

        /* passthrough this whole block */
        writer.Write(reader.ReadBytes((int)nextLength));

        /* pass throught the next 2 dwords */
        writer.Write(reader.ReadUInt32());
        writer.Write(reader.ReadUInt32());

        /* get offset and length of next section (not encrypted) */
        nextOffset = reader.ReadUInt32();
        nextLength = reader.ReadUInt32();

        writer.Write(nextOffset);
        writer.Write(nextLength);

        /* how many bytes from current position till the next offset */
        bytesToPassthru = nextOffset - msReader.Position;
        writer.Write(reader.ReadBytes((int)bytesToPassthru));

        /* passthrough this whole block */
        writer.Write(reader.ReadBytes((int)nextLength));

        /* passes printer name and disclaimer */
        var x = reader.ReadBytes((int)(CTB_DISCLAIMER_SIZE + printerNameLength));
        writer.Write(x);

        /* we're at the layer offset table now */
        var layerOffsets = new List<ulong>();
        var startOfTable = msReader.Position;
        ulong layerOffset = reader.ReadUInt64();
        ulong firstLayer = layerOffset;
        while (msReader.Position < (int)firstLayer)
        {
            layerOffsets.Add(layerOffset);
            reader.ReadUInt64();
            layerOffset = reader.ReadUInt64();
        }

        msReader.Position = (int)startOfTable;

        /* copy the rest of the file to the output memory stream, we'll decrypt layers next */
        writer.Write(reader.ReadBytes((int)(msReader.Length - msReader.Position)));

        byte[] cryptedFile = msWriter.ToArray();
        var layerCounter = 0;
        foreach (var offset in layerOffsets)
        {
            layerCounter++;
            msReader.Position = (int)(offset + 0x10);

            msReader.Position += 0x10;
            var encryptedOffset = reader.ReadUInt32();
            var encryptedLength = reader.ReadUInt32();
            msReader.Position -= 0x18;

            if (encryptedLength > 0)
            {
                Debug.WriteLine($"Layer {layerCounter} has {encryptedLength} bytes of encrypted data. (Layer data offset: {encryptedOffset})");
                var layerDataOffset = reader.ReadUInt32();

                msReader.Position = layerDataOffset + encryptedOffset;

                var encryptedLayerData = reader.ReadBytes((int)encryptedLength);
                var decryptedLayerData = CryptExtensions.AesCryptBytes(encryptedLayerData, Bigfoot, CipherMode.CBC, PaddingMode.None, encrypt, CookieMonster);

                Array.Copy(decryptedLayerData, 0, cryptedFile, layerDataOffset + encryptedOffset, encryptedLength);


                /* update encrypted markers in the layer header */
                cryptedFile.AsSpan((int)(layerDataOffset - 0x38), 8).Fill(0);
            }
            else
            {
                Debug.WriteLine($"Layer {layerCounter} is not encrypted");
            }
        }

        /* last 20 bytes are an encrypted sha256 */
        var cipheredHash = cryptedFile[^0x20..];

        var plainHash = CryptExtensions.AesCryptBytes(cipheredHash, Bigfoot, CipherMode.CBC, PaddingMode.None, encrypt, CookieMonster);
        Array.Copy(plainHash, 0, cryptedFile, cryptedFile.Length - 0x20, 0x20);

        File.WriteAllBytes(filePath, cryptedFile);
    }
    #endregion
}