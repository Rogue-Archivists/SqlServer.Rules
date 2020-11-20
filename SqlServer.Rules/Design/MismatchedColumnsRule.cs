using SqlServer.Rules.Globals;
using SqlServer.Dac;
using SqlServer.Dac.Visitors;
using Microsoft.SqlServer.Dac.CodeAnalysis;
using Microsoft.SqlServer.Dac.Model;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SqlServer.Rules.Design
{
    [ExportCodeAnalysisRule(RuleId,
        RuleDisplayName,
        Description = RuleDisplayName,
        Category = Constants.Design,
        RuleScope = SqlRuleScope.Model)]
    public sealed class MismatchedColumnsRule : BaseSqlCodeAnalysisRule
    {
        public const string RuleId = Constants.RuleNameSpace + "SRD0047";
        public const string RuleDisplayName = "Avoid using columns that match other columns by name, but are different in type or size.";
        private const string Message = "Column name {0} has {1} definition(s) accross {2} tables. The definition '{3}' differes from '{4}' by size or type";

        public MismatchedColumnsRule()
        {
        }

        public override IList<SqlRuleProblem> Analyze(SqlRuleExecutionContext ruleExecutionContext)
        {
            var problems = new List<SqlRuleProblem>();
            var sqlModel = ruleExecutionContext.SchemaModel;

            if (sqlModel == null)
                return problems;

            var tables = sqlModel.GetObjects(DacQueryScopes.UserDefined, Table.TypeClass).Where(t => !t.IsWhiteListed());
            var columnList = new List<TableColumnInfo>();

            foreach (var table in tables)
            {
                var fragment = table.GetFragment();
                var columnVisitor = new ColumnDefinitionVisitor();
                fragment.Accept(columnVisitor);
                columnList.AddRange(columnVisitor.NotIgnoredStatements(RuleId)
                    .Where(col => col.DataType != null)
                    .Select(col =>
                    new TableColumnInfo()
                    {
                        TableName = table.Name.GetName(),
                        ColumnName = col.ColumnIdentifier.Value,
                        DataType = col.DataType.Name.Identifiers.FirstOrDefault()?.Value,
                        DataTypeParameters = GetDataTypeLengthParameters(col),
                        Column = col,
                        Table = table
                    }
                ));
            }

            // condence information
            var subAgg = columnList
                .GroupBy(x => x, new KeyComparer() )
                .Select(x => (x.Key.ColumnName, x.Key.DataTypeInfo, TableCount: x.Count()));

            var ColumnNameStats = subAgg
                .Join(subAgg
                        .GroupBy(x => x.ColumnName, _comparer)
                        .Select(grp => new
                        {
                            ColumnName = grp.Key,
                            DefinitionCount = grp.Count(),
                            TotalColumns = grp.Sum(x => x.TableCount),
                            ModColumnCount = grp.Max(x => x.TableCount),
                            ModeColumnInfo = grp.OrderByDescending(x => x.TableCount).FirstOrDefault().DataTypeInfo
                        })
                        .Where(x => x.DefinitionCount != 1)
                        , x => x.ColumnName
                        , y => y.ColumnName
                         , (x, y) => new
                         {
                             x.ColumnName,
                             x.DataTypeInfo,
                             x.TableCount,
                             y.DefinitionCount,
                             y.TotalColumns,
                             y.ModColumnCount,
                             y.ModeColumnInfo
                         }
                )
                .Where(x => x.ModColumnCount != x.TableCount);

            var offenders = columnList
                   .Join(ColumnNameStats
                           , x => new { x.ColumnName, x.DataTypeInfo }
                           , y => new { y.ColumnName, y.DataTypeInfo }
                           , (x,y) =>
                           new {
                               x.Table,
                               x.Column,
                               x.TableName,
                               x.ColumnName,
                               x.DataTypeInfo,
                               y.DefinitionCount,
                               y.TableCount,
                               y.TotalColumns,
                               y.ModColumnCount,
                               y.ModeColumnInfo
                           }
                        );

            problems.AddRange( offenders
                                .Select(col =>
                                        new SqlRuleProblem(description: string.Format(Message, col.ColumnName, col.DefinitionCount.ToString(),
                                                                                    col.TableCount, col.DataTypeInfo, col.ModeColumnInfo),
                                                            modelElement: col.Table,
                                                            fragment: col.Column)
            ));

            return problems;
        }

        internal string GetDataTypeLengthParameters(ColumnDefinition col)
        {
            if (col.DataType is SqlDataTypeReference dataType)
            {
                return string.Join(",", dataType.GetDataTypeParameters());
            }
            return string.Empty;
        }


        private class TableColumnInfo
        {
            public string TableName { get; set; }
            public string ColumnName { get; set; }
            public string DataType { get; set; }
            public string DataTypeParameters { get; set; }
            public ColumnDefinition Column { get; set; }
            public TSqlObject Table { get; set; }

            public string DataTypeInfo
            {
               get
                {
                    return DataType +(string.IsNullOrEmpty(DataTypeParameters) ? string.Empty : "(" + DataTypeParameters.Replace("-1", "MAX") + ")");
                }
            }

            public override string ToString()
            {
                return $"{ColumnName} {DataType}{DataTypeInfo}";
            }
        }

        private class KeyComparer : IEqualityComparer<TableColumnInfo>
        {

            bool IEqualityComparer<TableColumnInfo>.Equals(TableColumnInfo x, TableColumnInfo y)
            {
                return x.ColumnName.Equals(y.ColumnName, StringComparison.InvariantCultureIgnoreCase) &&
                                       x.DataTypeInfo.Equals(y.DataTypeInfo, StringComparison.InvariantCultureIgnoreCase);
            }

            int IEqualityComparer<TableColumnInfo>.GetHashCode(TableColumnInfo obj)
            {
                int hash = (obj.ColumnName.GetHashCode() * 397);
                return hash ^ (obj.DataTypeInfo.GetHashCode());
            }
        }
    }
}