﻿using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading.Tasks;

namespace Microsoft.VisualStudio.ProjectSystem.VS.Input.Commands.Ordering
{
    /// <summary>
    /// This does proper file ordering for pasting or dropping items into a folder or project.
    /// </summary>
    [Export(typeof(IPasteDataObjectProcessor))]
    [Export(typeof(IPasteHandler))]
    [AppliesTo(ProjectCapabilities.SortByDisplayOrder)]
    [Order(OrderPrecedence)]
    internal class PasteOrdering : IPasteHandler, IPasteDataObjectProcessor
    {
        public const int OrderPrecedence = 10000;

        private readonly ConfiguredProject _configuredProject;
        private readonly IProjectAccessor _accessor;

        private IProjectTree _dropTarget;

        [ImportingConstructor]
        public PasteOrdering(UnconfiguredProject unconfiguredProject, IProjectAccessor accessor)
        {
            _configuredProject = unconfiguredProject.Services.ActiveConfiguredProjectProvider.ActiveConfiguredProject;
            _accessor = accessor;

            PasteHandlers = new OrderPrecedenceImportCollection<IPasteHandler>(projectCapabilityCheckProvider: unconfiguredProject);
            PasteProcessors = new OrderPrecedenceImportCollection<IPasteDataObjectProcessor>(projectCapabilityCheckProvider: unconfiguredProject);
        }

        [ImportMany]
        private OrderPrecedenceImportCollection<IPasteHandler> PasteHandlers { get; }

        [ImportMany]
        private OrderPrecedenceImportCollection<IPasteDataObjectProcessor> PasteProcessors { get; }

        private IPasteHandler PasteHandler
        {
            get
            {
                // Grab the paste handler that has the highest order precedence that is below PasteOrdering's order precedence. 
                IPasteHandler pasteHandler =
                    PasteHandlers.Where(x => x.Metadata.OrderPrecedence < OrderPrecedence)
                    .OrderByDescending(x => x.Metadata.OrderPrecedence).First().Value;

                return pasteHandler;
            }
        }

        private IPasteDataObjectProcessor PasteProcessor
        {
            get
            {
                // Grab the paste processor that has the highest order precedence that is below PasteOrdering's order precedence. 
                IPasteDataObjectProcessor pasteProcessor =
                    PasteProcessors.Where(x => x.Metadata.OrderPrecedence < OrderPrecedence)
                    .OrderByDescending(x => x.Metadata.OrderPrecedence).First().Value;

                return pasteProcessor;
            }
        }

        public bool CanHandleDataObject(object dataObject, IProjectTree dropTarget, IProjectTreeProvider currentProvider)
        {
            _dropTarget = dropTarget;
            return PasteProcessor.CanHandleDataObject(dataObject, dropTarget, currentProvider);
        }

        public Task<IEnumerable<ICopyPasteItem>> ProcessDataObjectAsync(object dataObject, IProjectTree dropTarget, IProjectTreeProvider currentProvider, DropEffects effect)
        {
            _dropTarget = dropTarget;
            return PasteProcessor.ProcessDataObjectAsync(dataObject, dropTarget, currentProvider, effect);
        }

        public DropEffects? QueryDropEffect(object dataObject, int grfKeyState, bool draggedFromThisProject)
        {
            return PasteProcessor.QueryDropEffect(dataObject, grfKeyState, draggedFromThisProject);
        }

        public Task ProcessPostFilterAsync(IEnumerable<ICopyPasteItem> items)
        {
            return PasteProcessor.ProcessPostFilterAsync(items);
        }

        public bool CanHandleItem(Type itemType)
        {
            return PasteHandler.CanHandleItem(itemType);
        }

        public void FilterItemList(IEnumerable<ICopyPasteItem> items, DropEffects effect)
        {
            PasteHandler.FilterItemList(items, effect);
        }

        public async Task<PasteItemsResult> PasteItemsAsync(IEnumerable<ICopyPasteItem> items, DropEffects effect)
        {
            Assumes.NotNull(_dropTarget);

            // ConfigureAwait is true because we need to come back for PasteItemsAsync to work. If not, PasteItemsAsync will throw.
            ImmutableHashSet<string> previousIncludes = await OrderingHelper.GetAllEvaluatedIncludes(_configuredProject, _accessor).ConfigureAwait(true);
            PasteItemsResult result = await PasteHandler.PasteItemsAsync(items, effect).ConfigureAwait(false);

            await OrderingHelper.Move(_configuredProject, _accessor, previousIncludes, _dropTarget, OrderingMoveAction.MoveToTop).ConfigureAwait(false);

            return result;
        }

        public bool PromptForAnyOverwrites(IEnumerable<ICopyPasteItem> items, ref DropEffects effect)
        {
            return PasteHandler.PromptForAnyOverwrites(items, ref effect);
        }

        public Task<IEnumerable<string>> ValidateItemListAsync(IEnumerable<ICopyPasteItem> items, DropEffects effect)
        {
            return PasteHandler.ValidateItemListAsync(items, effect);
        }
    }
}
