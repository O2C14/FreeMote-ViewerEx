﻿using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
// ReSharper disable InconsistentNaming

//PSB format is based on psbfile by number201724.

namespace FreeMote.Psb
{
    /// <summary>
    /// Packaged Struct Binary
    /// </summary>
    /// Photo Shop Big
    /// Pretty SB
    public class PSB
    {
        /// <summary>
        /// Header
        /// </summary>
        internal PsbHeader Header { get; set; }

        internal PsbArray Charset;
        internal PsbArray NamesData;
        internal PsbArray NameIndexes;
        /// <summary>
        /// Names
        /// </summary>
        public List<string> Names { get; set; }

        internal PsbArray StringOffsets;
        /// <summary>
        /// Strings
        /// </summary>
        public List<PsbString> Strings { get; set; }

        internal PsbArray ChunkOffsets;
        internal PsbArray ChunkLengths;
        /// <summary>
        /// Resource Chunk
        /// </summary>
        public List<PsbResource> Resources { get; set; }

        /// <summary>
        /// Objects (Entries)
        /// </summary>
        public PsbDictionary Objects { get; set; }

        internal PsbCollection ExpireSuffixList;
        public string Extension { get; internal set; }

        /// <summary>
        /// PSB Target Platform
        /// </summary>
        public PsbSpec Platform
        {
            get
            {
                var spec = Objects["spec"]?.ToString();
                if (string.IsNullOrEmpty(spec))
                {
                    return PsbSpec.other;
                }
                if (Enum.TryParse(spec, out PsbSpec p))
                {
                    return p;
                }
                return PsbSpec.other;
            }
            set => Objects["spec"] = new PsbString(value.ToString());
        }

        //private List<string> NamesCheck;

        public PSB(ushort version = 3)
        {
            Header = new PsbHeader() { Version = version };
        }

        public PSB(string path)
        {
            if (!File.Exists(path))
            {
                throw new IOException("File not exists.");
            }
            using (var fs = new FileStream(path, FileMode.Open))
            {
                LoadFromStream(fs);
            }
        }

        public PSB(Stream stream)
        {
            LoadFromStream(stream);
        }

        private void LoadFromStream(Stream stream)
        {
            BinaryReader br = new BinaryReader(stream, Encoding.UTF8);

            //Load Header
            Header = PsbHeader.Load(br);

            //Pre Load Strings
            br.BaseStream.Seek(Header.OffsetStrings, SeekOrigin.Begin);
            StringOffsets = new PsbArray(br.ReadByte() - (byte)PsbType.ArrayN1 + 1, br);
            Strings = new List<PsbString>();

            //Load Names
            br.BaseStream.Seek(Header.OffsetNames, SeekOrigin.Begin);
            Charset = new PsbArray(br.ReadByte() - (byte)PsbType.ArrayN1 + 1, br);
            NamesData = new PsbArray(br.ReadByte() - (byte)PsbType.ArrayN1 + 1, br);
            NameIndexes = new PsbArray(br.ReadByte() - (byte)PsbType.ArrayN1 + 1, br);
            LoadNames();

            //Pre Load Resources (Chunks)
            br.BaseStream.Seek(Header.OffsetChunkOffsets, SeekOrigin.Begin);
            ChunkOffsets = new PsbArray(br.ReadByte() - (byte)PsbType.ArrayN1 + 1, br);
            br.BaseStream.Seek(Header.OffsetChunkLengths, SeekOrigin.Begin);
            ChunkLengths = new PsbArray(br.ReadByte() - (byte)PsbType.ArrayN1 + 1, br);
            Resources = new List<PsbResource>(ChunkLengths.Value.Count);

            //Load Entries
            br.BaseStream.Seek(Header.OffsetEntries, SeekOrigin.Begin);
            IPsbValue obj;

            try
            {
                obj = Unpack(br);
                if (obj == null)
                {
                    throw new Exception("Can not parse objects");
                }
                Objects = obj as PsbDictionary ?? throw new Exception("Wrong offset when parsing objects");
            }
            catch (Exception e)
            {
                Debug.WriteLine(e);
                throw;
            }

            //if (Header.Version == 4)
            //{
            //    br.BaseStream.Seek(Header.OffsetUnknown1, SeekOrigin.Begin);
            //    var emptyArray1 = Unpack(br);
            //    br.BaseStream.Seek(Header.OffsetUnknown2, SeekOrigin.Begin);
            //    var emptyArray2 = Unpack(br);
            //    br.BaseStream.Seek(Header.OffsetResourceOffsets, SeekOrigin.Begin);
            //    var resArray = Unpack(br);
            //}

            if (ExpireSuffixList != null && ExpireSuffixList.Value.Count > 0)
            {
                var extStr = ExpireSuffixList.Value[0] as PsbString;
                if (extStr != null)
                {
                    Extension = extStr.Value;
                }
            }

            Resources.Sort((r1, r2) => (int)((r1.Index?? int.MaxValue) - (r2.Index?? int.MaxValue)));
        }

        /// <summary>
        /// Load a B Tree
        /// </summary>
        private void LoadNames()
        {
            Names = new List<string>(NameIndexes.Value.Count);
            for (int i = 0; i < NameIndexes.Value.Count; i++)
            {
                var list = new List<byte>();
                var index = NameIndexes[i];
                var chr = NamesData[(int)index];
                while (chr != 0)
                {
                    var code = NamesData[(int)chr];
                    var d = Charset[(int)code];
                    var realChr = chr - d;
                    //Debug.Write(realChr.ToString("X2") + " ");
                    chr = code;
                    //REF: https://stackoverflow.com/questions/18587267/does-list-insert-have-any-performance-penalty
                    list.Add((byte)realChr);
                }
                //Debug.WriteLine("");
                list.Reverse();
                var str = Encoding.UTF8.GetString(list.ToArray()); //That's why we don't use StringBuilder here.
                Names.Add(str);

                //Seems conflict
                //if (!Strings.ContainsKey(index))
                //{
                //    Strings.Add(index, new PsbString(str, index));
                //}
            }
            //NamesCheck = new List<string>(Names);
        }

        /// <summary>
        /// Unpack PSB Value
        /// </summary>
        /// <param name="br"></param>
        /// <returns></returns>
        private IPsbValue Unpack(BinaryReader br)
        {
            var typeByte = br.ReadByte();
            if (!Enum.IsDefined(typeof(PsbType), typeByte))
            {
                return null;
                //throw new ArgumentOutOfRangeException($"0x{type:X2} is not a known type.");
            }
            var type = (PsbType)typeByte;
            switch (type)
            {
                case PsbType.None:
                    return null;
                case PsbType.Null:
                    return new PsbNull();
                case PsbType.False:
                case PsbType.True:
                    return new PsbBool(type == PsbType.True);
                case PsbType.NumberN0:
                case PsbType.NumberN1:
                case PsbType.NumberN2:
                case PsbType.NumberN3:
                case PsbType.NumberN4:
                case PsbType.NumberN5:
                case PsbType.NumberN6:
                case PsbType.NumberN7:
                case PsbType.NumberN8:
                case PsbType.Float0:
                case PsbType.Float:
                case PsbType.Double:
                    return new PsbNumber(type, br);
                case PsbType.ArrayN1:
                case PsbType.ArrayN2:
                case PsbType.ArrayN3:
                case PsbType.ArrayN4:
                case PsbType.ArrayN5:
                case PsbType.ArrayN6:
                case PsbType.ArrayN7:
                case PsbType.ArrayN8:
                    return new PsbArray(typeByte - (byte)PsbType.ArrayN1 + 1, br);
                case PsbType.StringN1:
                case PsbType.StringN2:
                case PsbType.StringN3:
                case PsbType.StringN4:
                    var str = new PsbString(typeByte - (byte)PsbType.StringN1 + 1, br);
                    LoadString(ref str, br);
                    return str;
                case PsbType.ResourceN1:
                case PsbType.ResourceN2:
                case PsbType.ResourceN3:
                case PsbType.ResourceN4:
                    var res = new PsbResource(typeByte - (byte)PsbType.ResourceN1 + 1, br);
                    LoadResource(ref res, br);
                    return res;
                case PsbType.Collection:
                    return LoadCollection(br);
                case PsbType.Objects:
                    return LoadObjects(br);
                //Compiler used
                case PsbType.Integer:
                case PsbType.String:
                case PsbType.Resource:
                case PsbType.Decimal:
                case PsbType.Array:
                case PsbType.Boolean:
                case PsbType.BTree:
                    //Debug.WriteLine("FreeMote won't need these for compile.");
                    break;
                default:
                    return null;
            }
            return null;
        }

        private PsbDictionary LoadObjects(BinaryReader br)
        {
            var names = new PsbArray(br.ReadByte() - (byte)PsbType.ArrayN1 + 1, br);
            var offsets = new PsbArray(br.ReadByte() - (byte)PsbType.ArrayN1 + 1, br);
            var pos = br.BaseStream.Position;
            PsbDictionary dictionary = new PsbDictionary(names.Value.Count);
            for (int i = 0; i < names.Value.Count; i++)
            {
                br.BaseStream.Seek(pos, SeekOrigin.Begin);
                var name = Names[(int)names[i]];
                var offset = offsets[i];
                br.BaseStream.Seek(offset, SeekOrigin.Current);
                var obj = Unpack(br);
                if (obj != null)
                {
                    if (obj is IPsbCollection c)
                    {
                        c.Parent = dictionary;
                    }
                    dictionary.Value.Add(name, obj);
                }
                //Check
                //NamesCheck.Remove(name);
            }
            if (dictionary.Value.ContainsKey("expire_suffix_list"))
            {
                ExpireSuffixList = dictionary["expire_suffix_list"] as PsbCollection;
            }
            return dictionary;
        }

        /// <summary>
        /// Load a collection (unpack needed)
        /// </summary>
        /// <param name="br"></param>
        /// <returns></returns>
        private PsbCollection LoadCollection(BinaryReader br)
        {
            var offsets = new PsbArray(br.ReadByte() - (byte)PsbType.ArrayN1 + 1, br);
            var pos = br.BaseStream.Position;
            PsbCollection collection = new PsbCollection(offsets.Value.Count);
            for (int i = 0; i < offsets.Value.Count; i++)
            {
                var offset = offsets[i];
                br.BaseStream.Seek(offset, SeekOrigin.Current);
                var obj = Unpack(br);
                if (obj != null)
                {
                    if (obj is IPsbCollection c)
                    {
                        c.Parent = collection;
                    }
                    collection.Value.Add(obj);
                }
                br.BaseStream.Seek(pos, SeekOrigin.Begin);
            }
            return collection;
        }

        /// <summary>
        /// Load a resource based on index
        /// </summary>
        /// <param name="res"></param>
        /// <param name="br"></param>
        private void LoadResource(ref PsbResource res, BinaryReader br)
        {
            if (res.Index == null)
            {
                throw new IndexOutOfRangeException("Resource Index invalid");
            }
            //FIXED: Add check for re-used resources
            var resIndex = res.Index;
            var re = Resources.Find(r => r.Index == resIndex);
            if (re != null)
            {
                res = re;
                return; //Already loaded!
            }
            var pos = br.BaseStream.Position;
            var offset = ChunkOffsets[(int)res.Index];
            var length = ChunkLengths[(int)res.Index];
            br.BaseStream.Seek(Header.OffsetChunkData + offset, SeekOrigin.Begin);
            res.Data = br.ReadBytes((int)length);
            br.BaseStream.Seek(pos, SeekOrigin.Begin);
            Resources.Add(res);
        }

        /// <summary>
        /// Load a string based on index
        /// </summary>
        /// <param name="str"></param>
        /// <param name="br"></param>
        private void LoadString(ref PsbString str, BinaryReader br)
        {
            if (StringOffsets == null)
            {
                return;
            }
            var pos = br.BaseStream.Position;
            br.BaseStream.Seek(Header.OffsetStringsData + StringOffsets[(int)str.Index], SeekOrigin.Begin);
            var strValue = br.ReadStringZeroTrim();
            str.Value = strValue;
            br.BaseStream.Seek(pos, SeekOrigin.Begin);
            if (!Strings.Contains(str))
            {
                Strings.Add(str);
            }
            else
            {
                str = Strings.Find(s => s.Value == strValue);
            }
        }

        /// <summary>
        /// Update fields based on <see cref="Objects"/>
        /// </summary>
        public void Merge()
        {
            Names = new List<string>();
            Strings = new List<PsbString>();
            Collect(Objects);

            Names.Sort();
            UpdateStringsIndex();

            void Collect(IPsbValue obj)
            {
                switch (obj)
                {
                    case PsbString s:
                        Strings.Add(s);
                        break;
                    case PsbCollection c:
                        foreach (var o in c.Value)
                        {
                            Collect(o);
                        }
                        break;
                    case PsbDictionary d:
                        foreach (var pair in d.Value)
                        {
                            if (!Names.Contains(pair.Key))
                            {
                                Names.Add(pair.Key);

                                //Does Name appears in String Table?
                                //var psbStr = new PsbString(pair.Name);
                                //if (!Strings.ContainsValue(psbStr))
                                //{
                                //    psbStr.Index = count;
                                //    Strings.Add(psbStr.Index, psbStr);
                                //    count++;
                                //}
                            }

                            Collect(pair.Value);
                        }
                        break;
                    default:
                        break;
                }
            }
        }

        private void UpdateStringsIndex()
        {
            Strings.Sort((s1, s2) => (int)((s1.Index ?? int.MaxValue) - (s2.Index ?? int.MaxValue)));
            for (int i = 0; i < Strings.Count; i++)
            {
                Strings[i].Index = (uint)i;
            }
        }

        public byte[] Build()
        {
            /*
             * Header
             * --------------
             * Names (B Tree)
             * --------------
             * Entries
             * --------------
             * Strings
             * --------------
             * Resources
             * --------------
             */
            MemoryStream ms = new MemoryStream();
            BinaryWriter bw = new BinaryWriter(ms, Encoding.UTF8);
            bw.Pad((int)Header.GetHeaderLength());
            Header.HeaderLength = Header.GetHeaderLength();

            #region Compile Names
            //Mark Offset Names
            Header.OffsetNames = (uint)bw.BaseStream.Position;
            //Compile Names
            BTree.Build(Names, out var bNames, out var bTree, out var bOffsets);
            var nameArray = new PsbArray(bNames);
            nameArray.WriteTo(bw);
            var offsetArray = new PsbArray(bOffsets);
            offsetArray.WriteTo(bw);
            var treeArray = new PsbArray(bTree);
            treeArray.WriteTo(bw);
            #endregion

            #region Compile Entries
            Pack(bw, Objects);

            #endregion

            #region Compile Strings
            //Mark Offset Strings
            Header.OffsetStrings = (uint)bw.BaseStream.Position;

            using (var strMs = new MemoryStream())
            {
                List<uint> offsets = new List<uint>(Strings.Count);
                BinaryWriter strBw = new BinaryWriter(strMs);
                //Collect Strings
                for (var i = 0; i < Strings.Count; i++)
                {
                    var psbString = Strings[i];
                    offsets.Add((uint)strBw.BaseStream.Position);
                    strBw.WriteStringZeroTrim(psbString.Value);
                }
                strBw.Flush();
                StringOffsets = new PsbArray(offsets);
                StringOffsets.WriteTo(bw);
                Header.OffsetStringsData = (uint)bw.BaseStream.Position;
                bw.Write(strMs.ToArray());
            }

            #endregion

            #region Compile Resources

            Header.OffsetChunkOffsets = (uint)bw.BaseStream.Position;


            #endregion

            return ms.ToArray();
        }

        private void Pack(BinaryWriter bw, IPsbValue obj)
        {
            switch (obj)
            {
                case null:
                    return;
                case PsbNull pNull:
                    pNull.WriteTo(bw);
                    return;
                case PsbBool pBool:
                    pBool.WriteTo(bw);
                    return;
                case PsbNumber pNum:
                    pNum.WriteTo(bw);
                    return;
                case PsbArray pArr:
                    pArr.WriteTo(bw);
                    return;
                case PsbString pStr:
                    pStr.WriteTo(bw);
                    return;
                case PsbResource pRes:
                    pRes.WriteTo(bw);
                    return;
                case PsbCollection pCol:
                    SaveCollection(bw, pCol);
                    return;
                case PsbDictionary pDic:
                    SaveObjects(bw, pDic);
                    return;
               default:
                    return;
            }
        }


        private void SaveObjects(BinaryWriter bw, PsbDictionary pDic)
        {
            bw.Write((byte)pDic.Type);
            var namesList = new List<uint>();
            var indexList = new List<uint>();
            using (var ms = new MemoryStream())
            {
                BinaryWriter mbw = new BinaryWriter(ms);
                foreach (var pair in pDic.Value)
                {
                    var index = Names.BinarySearch(pair.Key);
                    if (index < 0)
                    {
                        throw new IndexOutOfRangeException($"Can not find Name [{pair.Key}] in Name Table");
                    }
                    namesList.Add((uint)index);
                    indexList.Add((uint)mbw.BaseStream.Position);
                    Pack(mbw, pair.Value);
                }
                mbw.Flush();
                new PsbArray(namesList).WriteTo(bw);
                new PsbArray(indexList).WriteTo(bw);
                bw.Write(ms.ToArray());
            }

        }

        /// <summary>
        /// Save a Collection
        /// </summary>
        /// <param name="bw"></param>
        /// <param name="pCol"></param>
        private void SaveCollection(BinaryWriter bw, PsbCollection pCol)
        {
            bw.Write((byte)pCol.Type);
            var indexList = new List<uint>(pCol.Value.Count);
            using (var ms = new MemoryStream())
            {
                BinaryWriter mbw = new BinaryWriter(ms);

                foreach (var obj in pCol.Value)
                {
                    indexList.Add((uint)mbw.BaseStream.Position);
                    Pack(mbw, obj);
                }
                mbw.Flush();
                new PsbArray(indexList).WriteTo(bw);
                bw.Write(ms.ToArray());
            }
        }
    }
}






