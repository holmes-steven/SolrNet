using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Xml.Linq;
using System.Xml.XPath;
using SolrNet.Utils;

namespace SolrNet.Impl.ResponseParsers
{
    /// <summary>
    /// Parses group.fields from query response
    /// </summary>
    /// <typeparam name="T">Document type</typeparam>
    public class GroupingResponseParser<T> : ISolrResponseParser<T>
    {

        private readonly ISolrDocumentResponseParser<T> docParser;

        private enum GroupingType { Field, Query, Unknown }

        public void Parse(XDocument xml, AbstractSolrQueryResults<T> results)
        {
            results.Switch(query: r => Parse(xml, r),
                           moreLikeThis: F.DoNothing);
        }

        public GroupingResponseParser(ISolrDocumentResponseParser<T> docParser)
        {
            this.docParser = docParser;
        }

        /// <summary>
        /// Parses the grouped elements
        /// </summary>
        /// <param name="xml"></param>
        /// <param name="results"></param>
        public void Parse(XDocument xml, SolrQueryResults<T> results)
        {

            var groupingType = GetGroupingType(xml);
            if (groupingType == GroupingType.Unknown)
            {
                return;
            }

            var mainGroupingNode = xml.Element("response")
                .Elements("lst")
                .FirstOrDefault(X.AttrEq("name", "grouped"));
            if (mainGroupingNode == null)
                return;

            var groupings =
                from groupNode in mainGroupingNode.Elements()
                let groupName = groupNode.Attribute("name").Value
                let groupResults = ParseGroupedResults(groupNode, groupingType)
                select new { groupName, groupResults };

            results.Grouping = groupings.ToDictionary(x => x.groupName, x => x.groupResults);
        }

        /// <summary>
        /// Gets the type of grouping.
        /// </summary>
        /// <param name="xml"></param>
        /// <returns></returns>
        private GroupingType GetGroupingType(XDocument xml) {

            if (xml.XPathSelectElement("//lst[@name='grouped']//arr[@name='groups']") != null) {
                return GroupingType.Field;
            }
            if (xml.XPathSelectElement("//lst[@name='grouped']/lst[@name]/int[@name='matches']/following-sibling::result[@name='doclist']") != null)
            {
                return GroupingType.Query;
            }

            return GroupingType.Unknown;
        }

        /// <summary>
        /// Parses the grouping of results.
        /// </summary>
        /// <param name="groupNode"></param>
        /// <param name="groupingType"></param>
        /// <returns></returns>
        private GroupedResults<T> ParseGroupedResults(XElement groupNode, GroupingType groupingType)
        {

            var ngroupNode = groupNode.Elements("int").FirstOrDefault(X.AttrEq("name", "ngroups"));
            var matchesValue = int.Parse(groupNode.Elements("int").First(X.AttrEq("name", "matches")).Value);

            ICollection<Group<T>> groups = new Collection<Group<T>>();
            if (groupingType == GroupingType.Field)
            {
                groups = ParseFieldGroups(groupNode).ToList();
            }
            if (groupingType == GroupingType.Query)
            {
                groups = new Collection<Group<T>> { ParseQueryGroup(groupNode) };
            }

            return new GroupedResults<T>
            {
                Groups = groups,
                Matches = matchesValue,
                Ngroups = ngroupNode == null ? null : (int?)int.Parse(ngroupNode.Value),
            };
        }

        /// <summary>
        /// Parses results grouped by field.
        /// </summary>
        /// <param name="node"></param>
        /// <returns></returns>
        private IEnumerable<Group<T>> ParseFieldGroups(XElement node)
        {

            return
                from docNode in node.Elements("arr").Where(X.AttrEq("name", "groups")).Elements()
                let groupValueNode = docNode.Elements().FirstOrDefault(X.AttrEq("name", "groupValue"))
                where groupValueNode != null
                let groupValue = groupValueNode.Name == "null"
                                     ? "UNMATCHED"
                                     : //These are the results that do not match the grouping
                                 groupValueNode.Value
                let resultNode = docNode.Elements("result").First(X.AttrEq("name", "doclist"))
                let numFound = Convert.ToInt32(resultNode.Attribute("numFound").Value)
                let docs = docParser.ParseResults(resultNode).ToList()
                select new Group<T>
                {
                    GroupValue = groupValue,
                    Documents = docs,
                    NumFound = numFound,
                };
        }

        /// <summary>
        /// Parses results grouped by query.
        /// </summary>
        /// <param name="node"></param>
        /// <returns></returns>
        private Group<T> ParseQueryGroup(XElement node)
        {

            XElement resultNode = node.Elements("result").First(X.AttrEq("name", "doclist"));

            string groupValue = node.Attribute("name").Value;
            int numFound = Convert.ToInt32(resultNode.Attribute("numFound").Value);
            var docs = docParser.ParseResults(resultNode).ToList();

            return new Group<T>
            {
                GroupValue = groupValue,
                NumFound = numFound,
                Documents = docs
            };
        }
    }
}