using System;
using Microsoft.CodeAnalysis.Diagnostics;
using Roslyn.Utilities;
using System.Collections.Immutable;
using System.IO;
using System.Text;

namespace Microsoft.CodeAnalysis
{
    public interface ICompilationRewriter
    {
        Compilation Rewrite(CompilationRewriterContext context);
    }

    public abstract class CompilationRewriter : DiagnosticAnalyzer, ICompilationRewriter
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray<DiagnosticDescriptor>.Empty;

        public override void Initialize(AnalysisContext context) {}

        public abstract Compilation Rewrite(CompilationRewriterContext context);
    }

    public sealed class CompilationRewriterContext
    {
        private readonly Action<Diagnostic> _reportDiagnostic;
        private const string DefaultSubFolder = "GeneratedFiles";
        private static readonly Encoding DefaultEncodingUTF8NoBom = new UTF8Encoding(false, false);
        
        internal CompilationRewriterContext(DiagnosticAnalyzer analyzer, Compilation compilation, string outputFolder, Action<Diagnostic> reportDiagnostic)
        {
            Compilation = compilation;
            OutputFolder = Path.Combine(outputFolder, DefaultSubFolder, analyzer.FileReference.GetAssembly().GetName().Name);
            _reportDiagnostic = reportDiagnostic;
            if (!Directory.Exists(OutputFolder))
            {
                Directory.CreateDirectory(OutputFolder);
            }
            Encoding = DefaultEncodingUTF8NoBom;
        }

        public Compilation Compilation { get; }

        public Encoding Encoding { get; set; }

        public string OutputFolder { get; }

        public string GetOutputFilePath(string fileNamePrefix, string fileExtension = null)
        {
            if (string.IsNullOrWhiteSpace(fileNamePrefix)) throw new ArgumentNullException(nameof(fileNamePrefix));
            var ext = fileExtension ?? ((Compilation.Language == LanguageNames.VisualBasic) ? ".vb" : ".cs");
            var fileName = $"{fileNamePrefix}{ext}";
            var path = PathUtilities.CombinePossiblyRelativeAndRelativePaths(OutputFolder, fileName);
            return path;
        }

        public void SaveSyntaxTree(SyntaxTree tree)
        {
            if (tree == null) throw new ArgumentNullException(nameof(tree));
            if (string.IsNullOrWhiteSpace(tree.FilePath)) throw new ArgumentException("The SyntaxTree must have a FilePath not null or empty", nameof(tree));

            SyntaxNode root;
            if (!tree.TryGetRoot(out root))
            {
                throw new ArgumentException("The SyntaxTree must have a root SyntaxNode", nameof(tree));
            }

            var directory = Path.GetDirectoryName(tree.FilePath);
            if (directory != null)
            {
                // Still try to create the folder if it does not exist
                // in case it is a sub-folder from OutputFolder or a whole different path
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
            }

            File.WriteAllText(tree.FilePath, root.ToFullString(), Encoding ?? DefaultEncodingUTF8NoBom);
        }
        
        public void ReportDiagnostic(Diagnostic diagnostic)
        {
            if (diagnostic == null) throw new ArgumentNullException(nameof(diagnostic));
            _reportDiagnostic(diagnostic);
        }
    }

    internal class CompilationRewriterDriver
    {
        public static Compilation Rewrite(ImmutableArray<DiagnosticAnalyzer> analyzers, Compilation compilation, string outputFolder, Action<Diagnostic> reportDiagnostic)
        {
            // Perform rewriting before analyzing for now 
            foreach (var rewriter in analyzers.OfType<ICompilationRewriter>())
            {
                var analyzer = (DiagnosticAnalyzer)rewriter;
                try
                {
                    var context = new CompilationRewriterContext(analyzer, compilation, outputFolder, reportDiagnostic);
                    compilation = rewriter.Rewrite(context);
                }
                catch (Exception e)
                {
                    // Use existing infrastructure for reporting error back
                    // Diagnostic for rewriter exception.
                    try
                    {
                        // Generate an error if an unexpected exception occured in a compilation rewriter
                        var diagnostic = AnalyzerExecutor.CreateAnalyzerExceptionDiagnostic(analyzer, e, null, DiagnosticSeverity.Error);
                        reportDiagnostic(diagnostic);
                    }
                    catch (Exception)
                    {
                        // Ignore exceptions from rewriter handlers.
                    }
                }
            }
            return compilation;
        }
    }
}
