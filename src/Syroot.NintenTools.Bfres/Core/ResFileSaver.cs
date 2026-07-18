using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Syroot.BinaryData;
using Syroot.Maths;

namespace Syroot.NintenTools.Bfres.Core
{
    /// <summary>
    /// Saves the hierachy and data of a <see cref="Bfres.ResFile"/>.
    /// </summary>
    public class ResFileSaver : BinaryDataWriter
    {
        // ---- CONSTANTS ----------------------------------------------------------------------------------------------

        /// <summary>
        /// Gets or sets a data block alignment typically seen with <see cref="Buffer.Data"/>.
        /// </summary>
        internal const uint AlignmentSmall = 0x40;

        // ---- FIELDS -------------------------------------------------------------------------------------------------

        private uint _ofsFileSize;
        private uint _ofsStringPool;
        private List<ItemEntry> _savedItems;
        private IDictionary<string, StringEntry> _savedStrings;
        private IDictionary<object, BlockEntry> _savedBlocks;

        // ---- CONSTRUCTORS & DESTRUCTOR ------------------------------------------------------------------------------

        /// <summary>
        /// Initializes a new instance of the <see cref="ResFileSaver"/> class saving data from the given
        /// <paramref name="resFile"/> into the specified <paramref name="stream"/> which is optionally left open.
        /// </summary>
        /// <param name="resFile">The <see cref="Bfres.ResFile"/> instance to save data from.</param>
        /// <param name="stream">The <see cref="Stream"/> to save data into.</param>
        /// <param name="leaveOpen"><c>true</c> to leave the stream open after writing, otherwise <c>false</c>.</param>
        internal ResFileSaver(ResFile resFile, Stream stream, bool leaveOpen)
            : base(stream, Encoding.ASCII, leaveOpen)
        {
            ByteOrder = ByteOrder.BigEndian;
            ResFile = resFile;
        }

        internal ResFileSaver(IResData resData, ResFile resFile, Stream stream, bool leaveOpen)
    : base(stream, Encoding.ASCII, leaveOpen)
        {
            ByteOrder = ByteOrder.BigEndian;
            ExportableData = resData;
            ResFile = resFile;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ResFileSaver"/> class for the file with the given
        /// <paramref name="fileName"/>.
        /// </summary>
        /// <param name="resFile">The <see cref="Bfres.ResFile"/> instance to save.</param>
        /// <param name="fileName">The name of the file to save the data into.</param>
        internal ResFileSaver(ResFile resFile, string fileName)
            : this(resFile, new FileStream(fileName, FileMode.Create, FileAccess.Write, FileShare.Read), false)
        {
        }

        internal ResFileSaver(IResData resData, ResFile resFile, string fileName)
    : this(resData, resFile, new FileStream(fileName, FileMode.Create, FileAccess.Write, FileShare.Read), false)
        {
        }


        // ---- PROPERTIES ---------------------------------------------------------------------------------------------

        /// <summary>
        /// Gets the saved <see cref="Bfres.ResFile"/> instance.
        /// </summary>
        internal ResFile ResFile { get; }

        /// <summary>
        /// Gets the saved <see cref="Bfres.IResData"/> instance used for exporting data.
        /// </summary>
        internal IResData ExportableData { get; }

        /// <summary>
        /// Gets the current index when writing lists or dicts.
        /// </summary>
        internal int CurrentIndex { get; private set; }

        // ---- METHODS (INTERNAL) -------------------------------------------------------------------------------------

        internal void WriteHeader(string SubSection, string Magic, int Offset = 32)
        {
            ByteOrder = ByteOrder.BigEndian;

            // Create queues fetching the names for the string pool and data blocks to store behind the headers.
            _savedItems = new List<ItemEntry>();
            _savedStrings = new SortedDictionary<string, StringEntry>(ResStringComparer.Instance);
            _savedBlocks = new Dictionary<object, BlockEntry>();

            //Write the header
            Write((byte)127);
            Write(Encoding.ASCII.GetBytes(SubSection));
            Write(ResFile.Version);
            WriteSignature(Magic);
            Write((int)(Offset - Position));
            Write(0);
            Write(0);
            ByteOrder = ByteOrder.BigEndian;
        }

        internal void WriteEndOfExportData()
        {
            _ofsStringPool = 0;

            SaveEntries();

            // Satisfy offsets, strings, and data blocks.
            WriteOffsets();


            WriteStrings();
            WriteBlocks();

            Flush();
        }

        internal void ExportSection(ShaderParamAnimType ParamAnimType)
        {
            if (ParamAnimType == ShaderParamAnimType.ShaderParameter)
            {
                WriteHeader("fresSUB", "FSHUPRMA");
                ((IResData)ExportableData).Save(this);
            }
            else if (ParamAnimType == ShaderParamAnimType.Color)
            {
                WriteHeader("fresSUB", "FSHUCLRA");
                ((IResData)ExportableData).Save(this);

            }
            else if (ParamAnimType == ShaderParamAnimType.TextureSRT)
            {
                WriteHeader("fresSUB", "FSHUSRTA");
                ((IResData)ExportableData).Save(this);
            }
        }

        internal void ExportSection(IResData VertexBuffer = null)
        {
            if (ExportableData is Model)
            {
                WriteHeader("fresSUB", "FMDL\0\0\0\0");
                ((IResData)ExportableData).Save(this);
            }
            else if (ExportableData is Skeleton)
            {
                WriteHeader("fresSUB", "FSKL\0\0\0\0");
                SaveSubFileFSKL((Skeleton)ExportableData, null);
            }
            else if (ExportableData is Bone)
            {
                WriteHeader("fmdlSUB", "FSKL\0\0\0\0");
                WriteSignature("BONE");
                ((IResData)ExportableData).Save(this);
            }
            else if (ExportableData is Material)
            {
                WriteHeader("fmdlSUB", "FMAT\0\0\0\0");
                ((IResData)ExportableData).Save(this);
            }
            else if (ExportableData is Shape)
            {
                WriteHeader("fmdlSUB", "FSHP\0\0\0\0");

                long ShapeOffPos = Position;
                Write(0);
                long VertexBufferOffPos = Position;
                Write(0);

                var offset = Position;
                using (TemporarySeek(ShapeOffPos, SeekOrigin.Begin))
                {
                    Write(offset);
                }
                ((IResData)ExportableData).Save(this);

                offset = Position;
                using (TemporarySeek(VertexBufferOffPos, SeekOrigin.Begin))
                {
                    Write(offset);
                }
                ((IResData)VertexBuffer).Save(this);

            }
            else if (ExportableData is SkeletalAnim)
            {
                WriteHeader("fresSUB", "FSKA\0\0\0\0");
                SaveSubFileFSKA((SkeletalAnim)ExportableData, null);
            }
            else if (ExportableData is TexPatternAnim)
            {
                WriteHeader("fresSUB", "FTXP\0\0\0\0");
                ((IResData)ExportableData).Save(this);
            }
            else if (ExportableData is SceneAnim)
            {
                WriteHeader("fresSUB", "FSCN\0\0\0\0");
                ((IResData)ExportableData).Save(this);
            }
            else if (ExportableData is CameraAnim)
            {
                WriteHeader("fresSUB", "FSCNFCAM");
                ((IResData)ExportableData).Save(this);
            }
            else if (ExportableData is LightAnim)
            {
                WriteHeader("fresSUB", "FSCNFLIT");
                ((IResData)ExportableData).Save(this);
            }
            else if (ExportableData is FogAnim)
            {
                WriteHeader("fresSUB", "FSCNFFOG");
                ((IResData)ExportableData).Save(this);
            }
            else if (ExportableData is Texture)
            {
                WriteHeader("fresSUB", "FTEX\0\0\0\0");
                ((IResData)ExportableData).Save(this);
            }
            else if (ExportableData is VisibilityAnim)
            {
                WriteHeader("fresSUB", "FVIS\0\0\0\0");
                ((IResData)ExportableData).Save(this);
            }

            WriteEndOfExportData();
        }

        /// <summary>
        /// Starts serializing the data from the <see cref="ResFile"/> root.
        /// </summary>
        internal void Execute()
        {

            // Create queues fetching the names for the string pool and data blocks to store behind the headers.
            _savedItems = new List<ItemEntry>();
            _savedStrings = new SortedDictionary<string, StringEntry>(ResStringComparer.Instance);
            _savedBlocks = new Dictionary<object, BlockEntry>();

            // Store the headers recursively and satisfy offsets to them, then the string pool and data blocks.
            SaveResFile();
            SaveSubFileFMDL();
            SaveSubFileFTEX();
            SaveSubFilesFSKA();
            //the rest?
            SaveSubFileFVIS(ResFile.BoneVisibilityAnims);
            SaveSubFileFVIS(ResFile.MatVisibilityAnims);
            SaveEntries();

            // Satisfy offsets, strings, and data blocks.

            WriteOffsets();
            WriteStrings();
            WriteBlocks();

            // Save final file size into root header at the provided offset.
            Position = _ofsFileSize;
            Write((uint)BaseStream.Length);
            Flush();
        }

        private void SaveResFile()
        {
            Save(ResFile, null);
            SaveDict(ResFile.Models, ResFile.ModelOffset);
            SaveDict(ResFile.Textures, ResFile.TextureOffset);
            SaveDict(ResFile.SkeletalAnims, ResFile.SkeletonAnimationOffset);
            SaveDict(ResFile.ShaderParamAnims, ResFile.ShaderParamAnimationOffset);
            SaveDict(ResFile.ColorAnims, ResFile.ColorParamAnimationOffset);
            SaveDict(ResFile.TexSrtAnims, ResFile.TexSrtParamAnimationOffset);
            SaveDict(ResFile.TexPatternAnims, ResFile.TexPatParamAnimationOffset);
            SaveDict(ResFile.BoneVisibilityAnims, ResFile.BoneVisAnimationOffset);
            SaveDict(ResFile.MatVisibilityAnims, ResFile.MatVisAnimationOffset);
            SaveDict(ResFile.ShapeAnims, ResFile.ShapeAnimationOffset);
            SaveDict(ResFile.SceneAnims, ResFile.SceneAnimationOffset);
            SaveDict(ResFile.ExternalFiles, ResFile.ExternalFileOffset);
        }

        private void SaveSubFileFMDL()
        {
            for (int i = 0; i < ResFile.Models.Count; ++i)
            {
                Model mdl = ResFile.Models[i];

                Save(mdl, ResFile.Models.GetSavedPos(i), i); // FMDL
                SaveList(mdl.VertexBuffers, mdl.VertexBufferOffset, mdl.Shapes, PosDictSaveType.SaveVertexBufferList); //FVTX

                SaveSubFileFSKL(mdl.Skeleton, mdl.SkeletonOffset);//FSKL

                SaveDict(mdl.Shapes, mdl.ShapeOffset);
                //SaveList(mdl.Shapes.Values, null);

                SaveDict(mdl.Materials, mdl.MaterialsOffset);
                //SaveList(mdl.Materials.Values, null);

                for (int j = 0; j < mdl.Shapes.Count; ++j)
                {
                    Shape shp = mdl.Shapes[j]; // FSHP

                    Save(shp, mdl.Shapes.GetSavedPos(j), j);

                    SaveList(shp.Meshes, shp.PosMeshArrayOffset); // LoD
                    SaveCustom(shp.SkinBoneIndices, () => { this.Write(shp.SkinBoneIndices); }, shp.PosSkinBoneIndicesOffset);
                    SaveList(shp.KeyShapes.Values, shp.PosKeyShapesOffset);
                    SaveList(shp.SubMeshBoundingNodes, shp.PosSubMeshBoundingNodesOffset);
                    SaveCustom(shp.SubMeshBoundings, () =>
                    {
                        foreach (var k in shp.SubMeshBoundings)
                        {
                            this.Write(k);
                        }
                    }, shp.PosSubMeshBoundingsOffset);
                    SaveCustom(shp.SubMeshBoundingIndices, () => { this.Write(shp.SubMeshBoundingIndices); }, shp.PosSubMeshBoundingsIndicesOffset);

                    foreach (var k in shp.Meshes) //LoD
                    {
                        SaveList(k.SubMeshes, k.PosSubMeshesOffset); //Visibility Group offset
                        Save(k.IndexBuffer, k.PosBufferOffset);
                    }
                }

                // vertex buffers
                foreach (var vtxbuf in mdl.VertexBuffers)
                {
                    SaveList(vtxbuf.Attributes.Values, vtxbuf.AttributeOffset, vtxbuf.Attributes);
                    SaveList(vtxbuf.Buffers, vtxbuf.BufferOffset);
                    SaveDict(vtxbuf.Attributes, vtxbuf.AttributeDictOffset);
                }

                // FMAT           
                for (int j = 0; j < mdl.Materials.Count; ++j)
                {
                    Material mat = mdl.Materials[j];

                    Save(mat, mdl.Materials.GetSavedPos(j), j);

                    SaveList(mat.RenderInfos.Values, null, mat.RenderInfos); // ??

                    Save(mat.RenderState, mat.PosRenderStateOffset);

                    SaveList(mat.TextureRefs, mat.PosTextureRefsOffset);
                    SaveList(mat.Samplers.Values, mat.PosSamplersOffset, mat.Samplers);
                    SaveList(mat.ShaderParams.Values, mat.PosShaderParamsOffset, mat.ShaderParams);
                    SaveCustom(mat.ShaderParamData, () => { this.Write(mat.ShaderParamData); }, mat.PosShaderParamDataOffset);
                    SaveCustom(mat.VolatileFlags, () => { this.Write(mat.VolatileFlags); }, mat.PosVolatileFlagsOffset);
                    Save(mat.ShaderAssign, mat.PosShaderAssignOffset);
                    SaveDict(mat.ShaderAssign.AttribAssigns, mat.ShaderAssign.PosAttribAssigns);
                    SaveDict(mat.ShaderAssign.SamplerAssigns, mat.ShaderAssign.PosSamplerAssigns);
                    SaveDict(mat.ShaderAssign.ShaderOptions, mat.ShaderAssign.PosShaderOptions);
                    SaveDict(mat.RenderInfos, mat.PosRenderInfoOffset);
                    SaveDict(mat.Samplers, mat.PosSamplerDictOffset);
                    SaveDict(mat.ShaderParams, mat.PosShaderParamDictOffset);
                    SaveDict(mat.UserData, mat.PosUserDataMaterialOffset);
                    SaveList(mat.UserData.Values, null, mat.UserData);
                }
            }
        }

        private void SaveSubFileFSKL(Skeleton skeleton, ResSavedPos saved_pos)
        {
            Save(skeleton, saved_pos);
            SaveList(skeleton.Bones.Values, skeleton.PosBoneArrayOffset, skeleton.Bones);
            SaveCustom(skeleton.MatrixToBoneList, () => { this.Write(skeleton.MatrixToBoneList); }, skeleton.PosMatrixToBoneListOffset);
            SaveCustom(skeleton.InverseModelMatrices, () => { this.Write(skeleton.InverseModelMatrices); }, skeleton.PosInverseModelMatricesOffset);
            SaveDict(skeleton.Bones, skeleton.PosBoneDictOffset);
        }

        private void SaveSubFileFTEX()
        {
            for (int i = 0; i < ResFile.Textures.Count; ++i)
            {
                Texture tex = ResFile.Textures[i];

                Save(tex, ResFile.Textures.GetSavedPos(i), i);
                SaveTextureReferencePositions(tex, _savedItems.Last()); // NOTE: taking last item for offsets


            }
        }


        // resolve texture references (FMDL for now)
        private void SaveTextureReferencePositions(Texture tex, ItemEntry item)
        {
            foreach (var mdl in ResFile.Models)
            {
                foreach (var mat in mdl.Value.Materials)
                {
                    for (int j = 0; j < mat.Value.TextureRefs.Count; ++j)
                    {
                        if (ReferenceEquals(mat.Value.TextureRefs[j].Texture, tex))
                        {
                            item.Offsets.Add(mat.Value.TextureRefs[j].PosTextureRef);
                        }
                    }
                }
            }
        }

        private void SaveSubFilesFSKA()
        {
            for (int i = 0; i < ResFile.SkeletalAnims.Count; ++i)
            {
                SkeletalAnim ska = ResFile.SkeletalAnims[i];
                SaveSubFileFSKA(ska, ResFile.SkeletalAnims.GetSavedPos(i), i);
            }
        }

        private void SaveSubFileFSKA(SkeletalAnim ska, ResSavedPos saved_pos, int index = -1)
        {
            Save(ska, saved_pos, index);

            SaveCustom(ska.BindIndices, () => { this.Write(ska.BindIndices); }, ska.PosBindIndicesOffset);

            SaveList(ska.BoneAnims, ska.PosBoneAnimsOffset);

            for (int j = 0; j < ska.BoneAnims.Count; ++j)
            {
                BoneAnim boneanim = ska.BoneAnims[j];

                SaveCustom(boneanim.BaseData, () => { boneanim.BaseData.Save(this, boneanim.FlagsBase); }, boneanim.PosBaseDataListOffset, true);
                SaveList(boneanim.Curves, boneanim.PosCurvesOffset, forceAdd: true);

                for (int k = 0; k < boneanim.Curves.Count; ++k)
                {
                    AnimCurve curve = boneanim.Curves[k];

                    SaveCustom(curve.Frames, () => { curve.SaveFrames(this); }, curve.PosFramesOffset);
                    SaveCustom(curve.Keys, () => { curve.SaveKeyData(this); }, curve.PosKeysOffset);
                }
            }

            SaveDict(ska.UserData, ska.PosUserDataOffset);
            SaveList(ska.UserData.Values, null, ska.UserData);
        }

        // bone visibility
        private void SaveSubFileFVIS(ResDict<VisibilityAnim> visdict)
        {
            for (int i = 0; i < visdict.Count; ++i)
            {
                VisibilityAnim visanim = visdict[i];
                Save(visanim, visdict.GetSavedPos(i), i);

                SaveCustom(visanim.BindIndices, () => { this.Write(visanim.BindIndices); }, visanim.PosBindIndicesOffset);

                SaveCustom(visanim.Names, () => { SaveStrings(visanim.Names); }, visanim.PosNamesOffset);

                SaveList(visanim.Curves, visanim.PosCurvesOffset, forceAdd: true); // force write offset
                SaveCustom(visanim.BaseDataList, () => { WriteBit32Booleans(visanim.BaseDataList); }, visanim.PosBaseDataOffset);

                for (int j = 0; j < visanim.Curves.Count; ++j)
                {
                    AnimCurve curve = visanim.Curves[j];

                    SaveCustom(curve.Frames, () => { curve.SaveFrames(this); }, curve.PosFramesOffset);
                    SaveCustom(curve.Keys, () => { curve.SaveKeyData(this); }, curve.PosKeysOffset);
                }


            }
        }

        internal void WriteBit32Booleans(bool[] booleans)
        {
            if (booleans.Length == 0) return;

            int idx = 0;
            while (idx < booleans.Length)
            {
                uint value = 0;
                for (int i = 0; i < 32; i++)
                {
                    if (booleans.Length <= idx) break;

                    if (booleans[idx])
                        value |= (uint)(1 << i);

                    idx++;
                }
                Write(value);
            }
        }

        private void SaveEntries()
        {

            // Store all queued items. Iterate via index as subsequent calls append to the list.
            for (int i = 0; i < _savedItems.Count; i++)
            {
                if (_savedItems[i].Target != null)
                {
                    // Ignore if it has already been written (list or dict elements).
                    continue;
                }

                ItemEntry entry = _savedItems[i];

                Align(4);
                switch (entry.Type)
                {
                    case ItemEntryType.List:
                        IEnumerable<IResData> list = (IEnumerable<IResData>)entry.Data;
                        // Check if the first item has already been written by a previous dict.
                        if (TryGetItemEntry(list.First(), ItemEntryType.ResData, out ItemEntry firstElement))
                        {
                            entry.Target = firstElement.Target;
                        }
                        else
                        {
                            entry.Target = (uint)Position;
                            CurrentIndex = 0;
                            foreach (IResData element in list)
                            {
                                _savedItems.Add(new ItemEntry(element, ItemEntryType.ResData, target: (uint)Position,
                                    index: CurrentIndex));
                                element.Save(this);
                                CurrentIndex++;
                            }
                        }
                        break;

                    case ItemEntryType.Dict:
                    case ItemEntryType.ResData:
                        entry.Target = (uint)Position;
                        CurrentIndex = entry.Index;
                        ((IResData)entry.Data).Save(this);
                        break;

                    case ItemEntryType.Custom:
                        entry.Target = (uint)Position;
                        entry.Callback.Invoke();
                        break;
                }
            }
        }

        internal void WriteOffset(long offset)
        {
            //The offset to point to
            long target = Position;

            //Seek to where to write the offset itself and use relative position
            using (TemporarySeek((uint)offset, SeekOrigin.Begin))
            {
                Write((uint)(target - offset));
            }
        }

        internal long SaveOffsetPos()
        {
            long OffsetPosition = Position;
            Write(0); //Fill offset space for later
            return OffsetPosition;
        }

        /// <summary>
        /// Reserves space for an offset to the <paramref name="resData"/> written later.
        /// </summary>
        /// <param name="resData">The <see cref="IResData"/> to save.</param>
        /// <param name="index">The index of the element, used for instances referenced by a <see cref="ResDict"/>.
        /// </param>
        [DebuggerStepThrough]
        internal void Save(IResData resData, ResSavedPos pos_value, int index = -1)
        {
            if (resData == null)
            {
                //Write(0);
                return;
            }

            if (TryGetItemEntry(resData, ItemEntryType.ResData, out ItemEntry entry))
            {
                if (pos_value != null)
                    entry.Offsets.Add((ResSavedPos)pos_value);
                entry.Index = index;
            }
            else
            {
                _savedItems.Add(new ItemEntry(resData, ItemEntryType.ResData, pos_value, index: index));
            }
            //Write(UInt32.MaxValue);
        }

        /// <summary>
        /// Reserves space for the <see cref="Bfres.ResFile"/> file size field which is automatically filled later.
        /// </summary>
        [DebuggerStepThrough]
        internal void SaveFieldFileSize()
        {
            _ofsFileSize = (uint)Position;
            Write(0);
        }

        /// <summary>
        /// Reserves space for the <see cref="Bfres.ResFile"/> string pool size and offset fields which are automatically
        /// filled later.
        /// </summary>
        [DebuggerStepThrough]
        internal void SaveFieldStringPool()
        {
            _ofsStringPool = (uint)Position;
            Write(0L);
        }

        private ResSavedPos SaveVertexBuffer(IResData buffer, ResDict<Shape> s)
        {
            foreach (var shape in s)
            {
                if (ReferenceEquals(buffer, shape.Value.VertexBuffer))
                    return shape.Value.PosVertexBufferOffset;
            }
            return null;
        }

        internal enum PosDictSaveType
        {
            Default,
            SaveVertexBufferList
        }

        /// <summary>
        /// Reserves space for an offset to the <paramref name="list"/> written later.
        /// </summary>
        /// <typeparam name="T">The type of the <see cref="IResData"/> elements.</typeparam>
        /// <param name="list">The <see cref="IList{T}"/> to save.</param>
        /// <param name="pos_value"></param>
        /// <param name="pos_dict"> If not null, will write positions of nodes to the provided Index Group ResDict.</param>
        //[DebuggerStepThrough]
        internal void SaveList<T>(IEnumerable<T> list, ResSavedPos pos_value, ResDict pos_dict = null, PosDictSaveType posdicttype = PosDictSaveType.Default, bool forceAdd = false)
            where T : IResData, new()
        {

            if (list?.Count() == 0)
            {
                if (forceAdd && list != null)
                {
                    if (pos_value != null)
                        _savedItems.Add(new ItemEntry(list, ItemEntryType.Custom, pos_value, callback: () => { }));
                }
                //Write(0);
                return;
            }

            // The offset to the list is the offset to the first element.
            if (TryGetItemEntry(list.First(), ItemEntryType.ResData, out ItemEntry entry))
            {
                if (pos_value != null)
                    entry.Offsets.Add((ResSavedPos)pos_value);
                entry.Index = 0;
            }
            else
            {
                // Queue all elements of the list.
                int index = 0;
                foreach (T element in list)
                {
                    ItemEntry newitem;

                    newitem = new ItemEntry(element, ItemEntryType.ResData, index: index);

                    if (index == 0)
                    {
                        // Add with offset to the first item for the list.
                        if (pos_value != null)
                            newitem.Offsets.Add(pos_value);
                    }

                    if (pos_dict != null)
                    {
                        ResSavedPos pos_get;

                        switch (posdicttype)
                        {
                            case PosDictSaveType.SaveVertexBufferList:
                                pos_get = SaveVertexBuffer(element, (ResDict<Shape>)pos_dict);
                                break;
                            default:
                                pos_get = pos_dict.GetSavedPos(index);
                                break;
                        }

                        if (pos_get != null)
                            newitem.Offsets.Add(pos_get);

                    }

                    _savedItems.Add(newitem);

                    index++;
                }
            }
            //Write(UInt32.MaxValue);
        }

        /// <summary>
        /// Reserves space for an offset to the <paramref name="dict"/> written later.
        /// </summary>
        /// <typeparam name="T">The type of the <see cref="IResData"/> element values.</typeparam>
        /// <param name="dict">The <see cref="ResDict{T}"/> to save.</param>
        [DebuggerStepThrough]
        internal void SaveDict<T>(ResDict<T> dict, ResSavedPos pos_value)
            where T : IResData, new()
        {
            if (dict?.Count == 0)
            {
                //Write(0);
                return;
            }
            if (TryGetItemEntry(dict, ItemEntryType.Dict, out ItemEntry entry))
            {
                if (pos_value != null)
                    entry.Offsets.Add((ResSavedPos)pos_value);
            }
            else
            {
                _savedItems.Add(new ItemEntry(dict, ItemEntryType.Dict, pos_value));
            }
            //Write(UInt32.MaxValue);
        }

        /// <summary>
        /// Reserves space for an offset to the <paramref name="data"/> written later with the
        /// <paramref name="callback"/>.
        /// </summary>
        /// <param name="data">The data to save.</param>
        /// <param name="callback">The <see cref="Action"/> to invoke to write the data.</param>
        [DebuggerStepThrough]
        internal void SaveCustom(object data, Action callback, ResSavedPos pos_value, bool forceAdd = false)
        {
            if (!forceAdd && data == null)
            {
                //Write(0);
                return;
            }

            // value types have the risk of not being written due to identical data, e.g. BoneAnimData 
            //forceAdd = !(data is IResData);

            if (!forceAdd && TryGetItemEntry(data, ItemEntryType.Custom, out ItemEntry entry))
            {
                if (pos_value != null)
                    entry.Offsets.Add((ResSavedPos)pos_value);
            }
            else
            {
                _savedItems.Add(new ItemEntry(data, ItemEntryType.Custom, pos_value, callback: callback));
            }
            //Write(UInt32.MaxValue);
        }

        /// <summary>
        /// Reserves space for an offset to the <paramref name="str"/> written later in the string pool with the
        /// specified <paramref name="encoding"/>.
        /// </summary>
        /// <param name="str">The name to save.</param>
        /// <param name="encoding">The <see cref="Encoding"/> in which the name will be stored.</param>
        [DebuggerStepThrough]
        internal void SaveString(string str, Encoding encoding = null)
        {
            if (str == null)
            {
                Write(0);
                return;
            }
            if (_savedStrings.TryGetValue(str, out StringEntry entry))
            {
                entry.Offsets.Add((uint)Position);
            }
            else
            {
                _savedStrings.Add(str, new StringEntry((uint)Position, encoding));
            }
            Write(UInt32.MaxValue);
        }

        /// <summary>
        /// Reserves space for offsets to the <paramref name="strings"/> written later in the string pool with the
        /// specified <paramref name="encoding"/>
        /// </summary>
        /// <param name="strings">The names to save.</param>
        /// <param name="encoding">The <see cref="Encoding"/> in which the names will be stored.</param>
        [DebuggerStepThrough]
        internal void SaveStrings(IEnumerable<string> strings, Encoding encoding = null)
        {
            foreach (string str in strings)
            {
                SaveString(str, encoding);
            }
        }

        /// <summary>
        /// Reserves space for an offset to the <paramref name="data"/> written later in the data block pool.
        /// </summary>
        /// <param name="data">The data to save.</param>
        /// <param name="alignment">The alignment to seek to before invoking the callback.</param>
        /// <param name="callback">The <see cref="Action"/> to invoke to write the data.</param>
        [DebuggerStepThrough]
        internal void SaveBlock(object data, uint alignment, Action callback)
        {
            if (data == null)
            {
                Write(0);
                return;
            }
            if (_savedBlocks.TryGetValue(data, out BlockEntry entry))
            {
                entry.Offsets.Add((uint)Position);
            }
            else
            {
                _savedBlocks.Add(data, new BlockEntry((uint)Position, alignment, callback));
            }
            Write(UInt32.MaxValue);
        }

        /// <summary>
        /// Writes a BFRES signature consisting of 4 ASCII characters encoded as an <see cref="UInt32"/>.
        /// </summary>
        /// <param name="value">A valid signature.</param>
        internal void WriteSignature(string value)
        {
            Write(Encoding.ASCII.GetBytes(value));
        }

        // ---- METHODS (PRIVATE) --------------------------------------------------------------------------------------

        private bool TryGetItemEntry(object data, ItemEntryType type, out ItemEntry entry)
        {
            //entry = _savedItems.Find(x => x.Data.Equals(data) && x.Type == type);
            //return entry != null;                

            foreach (ItemEntry savedItem in _savedItems)
            {
                if (savedItem.Data == null)
                    continue;

                if (savedItem.Data.Equals(data) && savedItem.Type == type)
                {
                    entry = savedItem;
                    return true;
                }
            }


            entry = null;
            return false;

        }

        private void WriteStrings()
        {
            // Sort the strings ordinally.
            SortedList<string, StringEntry> sorted = new SortedList<string, StringEntry>(ResStringComparer.Instance);
            foreach (KeyValuePair<string, StringEntry> entry in _savedStrings)
            {
                sorted.Add(entry.Key, entry.Value);
            }

            Align(4);
            uint stringPoolOffset = (uint)Position;

            foreach (KeyValuePair<string, StringEntry> entry in sorted)
            {
                // Align and satisfy offsets.
                Write(entry.Key.Length);
                using (TemporarySeek())
                {
                    SatisfyOffsets(entry.Value.Offsets, (uint)Position);
                }

                // Write the name.
                Write(entry.Key, BinaryStringFormat.ZeroTerminated, entry.Value.Encoding ?? Encoding);
                Align(4);
            }
            BaseStream.SetLength(Position); // Workaround to make last alignment expand the file if nothing follows.

            if (_ofsStringPool != 0)
            {
                // Save string pool offset and size in main file header.
                uint stringPoolSize = (uint)(Position - stringPoolOffset);
                using (TemporarySeek(_ofsStringPool, SeekOrigin.Begin))
                {
                    Write(stringPoolSize);
                    Write((int)(stringPoolOffset - Position));
                }
            }
        }

        private void WriteBlocks()
        {
            foreach (KeyValuePair<object, BlockEntry> entry in _savedBlocks)
            {
                // Align and satisfy offsets.
                if (entry.Value.Alignment != 0) Align((int)entry.Value.Alignment);
                using (TemporarySeek())
                {
                    SatisfyOffsets(entry.Value.Offsets, (uint)Position);
                }

                // Write the data.
                entry.Value.Callback.Invoke();
            }
        }

        private void WriteOffsets()
        {
            using (TemporarySeek())
            {
                foreach (ItemEntry entry in _savedItems)
                {
                    if (entry.Target != null)
                        SatisfyOffsets(entry.Offsets.ConvertAll(x => x.Value), entry.Target.Value);
                }
            }
        }

        private void SatisfyOffsets(IEnumerable<uint> offsets, uint target)
        {
            foreach (uint offset in offsets)
            {
                Position = offset;
                Write((int)(target - offset));
            }
        }

        // ---- STRUCTURES ---------------------------------------------------------------------------------------------

        [DebuggerDisplay("{" + nameof(Type) + "} {" + nameof(Data) + "}")]
        private class ItemEntry
        {
            internal object Data;
            internal ItemEntryType Type;
            internal List<ResSavedPos> Offsets;
            internal uint? Target;
            internal Action Callback;
            internal int Index;

            internal ItemEntry(object data, ItemEntryType type, ResSavedPos offset = null, uint? target = null,
                Action callback = null, int index = -1)
            {
                Data = data;
                Type = type;
                Offsets = new List<ResSavedPos>();
                if (offset != null) // Might be null for enumerable entries to resolve references to them later.
                {
                    Offsets.Add((ResSavedPos)offset);
                }
                Callback = callback;
                Target = target;
                Index = index;
            }
        }

        private enum ItemEntryType
        {
            List, Dict, ResData, Custom
        }

        private class StringEntry
        {
            internal List<uint> Offsets;
            internal Encoding Encoding;

            internal StringEntry(uint offset, Encoding encoding = null)
            {
                Offsets = new List<uint>(new uint[] { offset });
                Encoding = encoding;
            }
        }

        private class BlockEntry
        {
            internal List<uint> Offsets;
            internal uint Alignment;
            internal Action Callback;

            internal BlockEntry(uint offset, uint alignment, Action callback)
            {
                Offsets = new List<uint> { offset };
                Alignment = alignment;
                Callback = callback;
            }
        }
    }
}
