﻿/* Credit to the following for ideas/code in this file:
 * Limey for the C# Metadata reader (http://www.mobileread.com/forums/showthread.php?t=185565)
 * KindleUnpack (http://www.mobileread.com/forums/showthread.php?t=61986)
 * MobileRead Wiki (http://wiki.mobileread.com/wiki/MOBI)
 */ 

using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;

namespace XRayBuilderGUI.Unpack
{
    class Metadata
    {
        public PDBHeader PDB;
        public PalmDOCHeader PDH;
        public MobiHead mobiHeader;
        private int _startRecord = 1;

        public Metadata(FileStream fs)
        {
            fs.Seek(0, SeekOrigin.Begin);
            PDB = new PDBHeader(fs);
            PDH = new PalmDOCHeader(fs);
            mobiHeader = new MobiHead(fs, PDB.MobiHeaderSize);

            // Start at end of first book records, search for a second (KF8) and use it instead (for combo books)
            for (int i = PDH.RecordCount; i < PDB.NumRecords - 1; i++)
            {
                uint recSize = PDB._recInfo[i + 1].RecordDataOffset - PDB._recInfo[i].RecordDataOffset;
                if (recSize < 8) continue;
                byte[] buffer = new byte[recSize];
                fs.Seek(PDB._recInfo[i].RecordDataOffset, SeekOrigin.Begin);
                fs.Read(buffer, 0, buffer.Length);
                if (Encoding.ASCII.GetString(buffer, 0, 8) == "BOUNDARY")
                {
                    _startRecord = i + 2;
                    PDH = new PalmDOCHeader(fs);
                    mobiHeader = new MobiHead(fs, PDB.MobiHeaderSize);
                    break;
                }
            }
        }

        public byte[] getRawML(FileStream fs)
        {
            Decompressor decomp;
            switch (PDH.Compression)
            {
                case (1):
                    decomp = new UncompressedReader();
                    break;
                case (2):
                    decomp = new PalmDOCReader();
                    break;
                case (17480):
                    HUFFCDICReader reader = new HUFFCDICReader();
                    try
                    {
                        int recOffset = (int)mobiHeader.HuffmanRecordOffset;
                        byte[] huffSect = new byte[PDB._recInfo[recOffset + 1].RecordDataOffset - PDB._recInfo[recOffset].RecordDataOffset];
                        fs.Seek(PDB._recInfo[recOffset].RecordDataOffset, SeekOrigin.Begin);
                        fs.Read(huffSect, 0, huffSect.Length);
                        reader.loadHuff(huffSect);
                        int recCount = (int)mobiHeader.HuffmanRecordCount;
                        for (int i = 1; i < recCount; i++)
                        {
                            huffSect = new byte[PDB._recInfo[recOffset + i + 1].RecordDataOffset - PDB._recInfo[recOffset + i].RecordDataOffset];
                            fs.Seek(PDB._recInfo[recOffset + i].RecordDataOffset, SeekOrigin.Begin);
                            fs.Read(huffSect, 0, huffSect.Length);
                            reader.loadCdic(huffSect);
                        }
                    } catch (Exception ex)
                    {
                        throw new Exception("Error in HUFF/CDIC decompression: " + ex.Message);
                    }
                    decomp = reader;
                    break;
                default:
                    throw new Exception("Unknown compression type " + PDH.Compression + ".");
            }
            byte[] rawML = new byte[0];
            int endRecord = _startRecord + PDH.RecordCount -1;
            for (int i = _startRecord; i <= endRecord; i++)
            {
                byte[] buffer = new byte[PDB._recInfo[i + 1].RecordDataOffset - PDB._recInfo[i].RecordDataOffset];
                fs.Seek(PDB._recInfo[i].RecordDataOffset, SeekOrigin.Begin);
                fs.Read(buffer, 0, buffer.Length);
                buffer = trimTrailingDataEntries(buffer);
                byte[] result = decomp.unpack(buffer, PDB.MobiHeaderSize);
                rawML = rawML.Concat(result).ToArray();
            }
            return rawML;
        }

        private byte[] trimTrailingDataEntries(byte[] data)
        {
            for (int i = 0; i < mobiHeader.trailers; i++)
            {
                int num = getSizeOfTrailingDataEntry(data);
                byte[] temp = new byte[data.Length - num];
                Array.Copy(data, temp, temp.Length);
                data = temp;
            }
            if (mobiHeader.multibyte)
            {
                int num = (data[data.Length - 1] & 3) + 1;
                byte[] temp = new byte[data.Length - num];
                Array.Copy(data, temp, temp.Length);
                data = temp;
            }
            return data;
        }

        private int getSizeOfTrailingDataEntry(byte[] data)
        {
            int num = 0;
            for (int i = data.Length - 4; i < data.Length; i++)
            {
                if ((data[i] & 0x80) == 0x80)
                    num = 0;
                num = (num << 7) | (data[i] & 0x7f);
            }
            return num;
        }
    }
}