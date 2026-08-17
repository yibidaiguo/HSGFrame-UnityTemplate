using System.Linq;
using HSGFrame.GraphAdapter;
using NodeEditor.EditorUI;
using NUnit.Framework;

namespace Template.Tests.GraphAuthoring.EditMode
{
    public class GraphAuthoringBoundaryTests
    {
        [Test]
        public void CanonicalFacadeComesFromNodeEditorEditorAssembly()
        {
            Assert.That(
                typeof(GraphAuthoringAssetAccess).Assembly.GetName().Name,
                Is.EqualTo("NodeEditor.Editor"));
        }

        [Test]
        public void CompatibilityRuntimeDoesNotReferenceEditorAssembly()
        {
            var references = typeof(GraphDocument).Assembly
                .GetReferencedAssemblies()
                .Select(assembly => assembly.Name);

            Assert.That(references, Does.Not.Contain("NodeEditor.Editor"));
        }
    }
}
