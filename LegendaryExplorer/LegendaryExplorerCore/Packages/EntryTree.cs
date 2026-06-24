using System.Collections;
using System.Collections.Generic;
using System.Linq;
using EntryTreeNode = LegendaryExplorerCore.Unreal.BinaryConverters.TreeNode<LegendaryExplorerCore.Packages.IEntry, int>;

namespace LegendaryExplorerCore.Packages
{
    public class EntryTree : IEnumerable<IEntry>
    {
        public EntryTree(IMEPackage pcc)
        {
            imports = new List<EntryTreeNode>(pcc.ImportCount);
            foreach (ImportEntry import in pcc.Imports)
            {
                imports.Add(new EntryTreeNode(import));
            }

            exports = new List<EntryTreeNode>(pcc.ExportCount);
            foreach (ExportEntry export in pcc.Exports)
            {
                exports.Add(new EntryTreeNode(export));
            }

            root = new List<EntryTreeNode>();

            foreach (EntryTreeNode node in exports.Concat(imports))
            {
                int idxLink = node.Data.idxLink;
                if (idxLink is 0)
                {
                    root.Add(node);
                }
                else
                {
                    this[idxLink]?.Children.Add(node.Data.UIndex);
                }
            }
        }

        private readonly List<EntryTreeNode> imports;
        private readonly List<EntryTreeNode> exports;
        private readonly List<EntryTreeNode> root;

        public EntryTreeNode this[int uIndex]
        {
            get
            {
                if (uIndex > 0 && uIndex <= exports.Count)
                {
                    return exports[uIndex - 1];
                }

                uIndex = -uIndex - 1;
                if (uIndex >= 0 && uIndex < imports.Count)
                {
                    return imports[uIndex];
                }

                return null;
            }
        }

        public void Add(ExportEntry exp)
        {
            var node = new EntryTreeNode(exp);
            exports.Add(node);
            int link = exp.idxLink;
            if (link == 0)
            {
                root.Add(node);
            }
            else
            {
                this[exp.idxLink].Children.Add(exports.Count);
            }
        }

        public void Add(ImportEntry imp)
        {
            var node = new EntryTreeNode(imp);
            imports.Add(node);
            int link = imp.idxLink;
            if (link == 0)
            {
                root.Add(node);
            }
            else
            {
                this[link].Children.Add(-imports.Count);
            }
        }

        public IEnumerable<EntryTreeNode> Roots => root;

        public int NumChildrenOf(IEntry entry) => this[entry.UIndex]?.Children.Count ?? 0;

        public int NumChildrenOf(int uIndex) => this[uIndex]?.Children.Count ?? 0;

        public IEnumerable<IEntry> GetDirectChildrenOf(IEntry entry) => GetDirectChildrenOf(entry.UIndex);

        public IEnumerable<IEntry> GetDirectChildrenOf(int uIndex)
        {
            EntryTreeNode node = this[uIndex];
            if (node is null)
            {
                yield break;
            }

            foreach (int i in node.Children.ToArray())
            {
                yield return this[i].Data;
            }
        }

        public List<IEntry> FlattenTreeOf(IEntry entry, bool includeRoot = true) => FlattenTreeOf(entry.UIndex, includeRoot);

        public List<IEntry> FlattenTreeOf(int uIndex, bool includeRoot = true)
        {
            var entries = new List<IEntry>();
            if (includeRoot)
            {
                entries.Add(this[uIndex].Data);
            }
            foreach (int i in this[uIndex])
            {
                entries.AddRange(FlattenTreeOf(i));
            }

            return entries;
        }

        public IEntry GetEntry(int uIndex)
        {
            return this[uIndex].Data;
        }

        public IEnumerator<IEntry> GetEnumerator()
        {
            foreach (EntryTreeNode node in exports)
            {
                yield return node.Data;
            }
            foreach (EntryTreeNode node in imports)
            {
                yield return node.Data;
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}