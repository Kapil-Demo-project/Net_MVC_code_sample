using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using API.core.Attributes;
using API.core.Models;
using API.core.Utils;
using API.core.Utils.Cases;
using AutoMapper;
using Core.Db.Helpers;
using Core.Db.Models;
using Core.Db.Mongo;
using Core.Helpers.FieldsSettings;
using Core.Helpers.Mongo;
using Core.Helpers.Permissions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http.Headers;
using Microsoft.Net.Http.Headers;
using Core.Helpers.Helpers;
using Core.Helpers.Translations;
using MongoDB.Bson;
using Nest;
using Core.Helpers.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Core.Helpers.GeneralViewModels;
using Core.Helpers.Querying;
using Core.Helpers.Querying.Mongo;
using Core.Helperers.Extensions;
using API.core.Utils.Searching;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Core.Helpers.Querying.ES;
using Microsoft.Extensions.Options;
using Core.Db;
using Microsoft.EntityFrameworkCore;
using Core.Helpers.General;
using Core.Helpers.Formatting;
using API.core.Utils.Items;
using Core.Helpers.Tasks;
using Core.Helpers.AutoDispo;
using Core.Helpers.QueueAccess;
using Core.Db.ElasticSearch;
using API.core.Utils.Tags;
using Core.Helpers.ElasticSearch;
using Microsoft.Extensions.Logging;
using System.Linq.Dynamic.Core;
using Core.Helpers.Media;
using API.core.Utils.Evidence.com;
using System.Text.RegularExpressions;
using Core.Helpers.Extensions;
using System.Diagnostics;
using Core.Helpers.Automapper;
using static Core.Helpers.Permissions.CaseLevelPermissionsHelper;
using API.core.Utils.Evidence.com.Models;

namespace API.core.Controllers
{

    [Authorize]
    [Produces("application/json")]
    [ModelType(ModelType.Cases)]
    [PermissionsEntityType(PermissionsEntityType.Case)]
    [Route("api/[controller]")]
    [ApiController]
    public class CasesController : TpApiControllerBase
    {
        private const long MaxCaseIdLength = 1000;
        private const long MaxUserSelectedItemIds = 1000;
        private const int MaxMiniCasesLength = 1000;
        private const int MaxTranactionalMediaCount = 999;

        private readonly IMongoCaseHandler mongoCaseHandler;
        private readonly IPermissionsHelper permissionsHelper;
        private readonly ICaseDisplayFormatter caseDisplayFormatter;
        private readonly IRecentCasesHelper recentCasesHelper;
        private readonly ICaseUpdateBusinessLogic caseUpdateBusLogic;
        private readonly ICopyCaseVmToBsonDoc copyCaseVmToBsonDoc;
        private readonly ICaseHelper caseHelper;
        private readonly IWorkflowQueueWriter workflowQueueWriter;
        private readonly ICaseAddBusinessLogic newCaseBusinessLogic;
        private readonly ICopyFormsToBsonDoc copyFormsToBsonDoc;
        private readonly ICaseLevelPermissionsHelper caseLevelPermissionsHelper;
        private readonly IGetCaseListForItemView getCaseListForItemView;
        private readonly IMongoItemHandler mongoItemHandler;
        private readonly ICaseHistoryHelper caseHistoryHelper;
        private readonly ISearchModelValidation searchModelValidation;
        private readonly IMongoSearch mongoSearch;
        private readonly IMongoQueryBuilderGeneric<CaseHistoryMongo> historyQueryBuilder;
        private readonly IMongoQueryBuilderGeneric<CaseViewModel> caseQueryBuilder;
        private readonly IEsQueryBuilder<CaseViewModelList> caseEsQueryBuilder;
        private readonly ElasticSearchConfigWrapper esConfig;
        private readonly TrackerContext db;
        private readonly IDataVisibilityHelper dataVisibilityHelper;
        private readonly IItemDisplayFormatter itemDisplayFormatter;
        private readonly ICaseItemHelper caseItemHelper;
        private readonly ICaseMediaHelper caseMediaHelper;
        private readonly IAutoDispoHelper autoDispoHelper;
        private readonly ICaseAutoDispoHelper caseAutoDispoHelper;
        private readonly ITranslationsHelperBase translationsHelperBase;
        private readonly IRequestHelper requestHelper;
        private readonly ICaseMassUpdate caseMassUpdate;
        private readonly ICaseTagUpdateHelper tagUpdateHelper;
        private readonly IMapper mapper;
        private readonly IFormHelper formHelper;
        private readonly ICaseAddAndEditQueueWriter caseAddAndEditQueueWriter;
        private readonly ILogger<CasesController> logger;
        private readonly ICaseMediaCtrlHelper caseMediaCtrlHelper;
        private readonly IMediaHelper mediaHelper;
        private readonly IEcWorkflowFactory ecWorkflowFactory;
        private readonly IEcGenUtils ecGenUtils;

        public CasesController(IApiControllerWrapper wrapper,
            IMongoCaseHandler mongoCaseHandler,
            IPermissionsHelper permissionsHelper,
            ICaseDisplayFormatter caseDisplayFormatter,
            IRecentCasesHelper recentCasesHelper,
            ICaseUpdateBusinessLogic caseUpdateBusLogic,
            ICopyCaseVmToBsonDoc copyCaseVmToBsonDoc,
            ICaseHelper caseHelper,
            IWorkflowQueueWriter workflowQueueWriter,
            ICaseAddBusinessLogic newCaseBusinessLogic,
            ICopyFormsToBsonDoc copyFormsToBsonDoc,
            ICaseLevelPermissionsHelper caseLevelPermissionsHelper,
            IGetCaseListForItemView getCaseListForItemView,
            IMongoItemHandler mongoItemHandler,
            ICaseHistoryHelper caseHistoryHelper,
            ISearchModelValidation searchModelValidation,
            IMongoSearch mongoSearch,
            IMongoQueryBuilderGeneric<CaseHistoryMongo> historyQueryBuilder,
            IMongoQueryBuilderGeneric<CaseViewModel> caseQueryBuilder,
            IEsQueryBuilder<CaseViewModelList> caseEsQueryBuilder,
            IOptions<ElasticSearchConfigWrapper> esConfig,
            TrackerContext db,
            IDataVisibilityHelper dataVisibilityHelper,
            IItemDisplayFormatter itemDisplayFormatter,
            ICaseItemHelper caseItemHelper,
            ICaseMediaHelper caseMediaHelper,
            IAutoDispoHelper autoDispoHelper,
            ICaseAutoDispoHelper caseAutoDispoHelper,
            ITranslationsHelperBase translationsHelperBase,
            IRequestHelper requestHelper,
            ICaseMassUpdate caseMassUpdate,
            ICaseTagUpdateHelper tagUpdateHelper,
            IMapper mapper,
            IFormHelper formHelper,
            ICaseAddAndEditQueueWriter caseAddAndEditQueueWriter,
            ILogger<CasesController> logger,
            ICaseMediaCtrlHelper caseMediaCtrlHelper,
            IMediaHelper mediaHelper,
            IEcWorkflowFactory ecWorkflowFactory,
            IEcGenUtils ecGenUtils)
            : base(wrapper)
        {
            this.mongoCaseHandler = mongoCaseHandler;
            this.permissionsHelper = permissionsHelper;
            this.caseDisplayFormatter = caseDisplayFormatter;
            this.recentCasesHelper = recentCasesHelper;
            this.caseUpdateBusLogic = caseUpdateBusLogic;
            this.copyCaseVmToBsonDoc = copyCaseVmToBsonDoc;
            this.caseHelper = caseHelper;
            this.workflowQueueWriter = workflowQueueWriter;
            this.newCaseBusinessLogic = newCaseBusinessLogic;
            this.copyFormsToBsonDoc = copyFormsToBsonDoc;
            this.caseLevelPermissionsHelper = caseLevelPermissionsHelper;
            this.getCaseListForItemView = getCaseListForItemView;
            this.mongoItemHandler = mongoItemHandler;
            this.caseHistoryHelper = caseHistoryHelper;
            this.searchModelValidation = searchModelValidation;
            this.mongoSearch = mongoSearch;
            this.historyQueryBuilder = historyQueryBuilder;
            this.caseQueryBuilder = caseQueryBuilder;
            this.caseEsQueryBuilder = caseEsQueryBuilder;
            this.esConfig = esConfig.Value;
            this.db = db;
            this.dataVisibilityHelper = dataVisibilityHelper;
            this.itemDisplayFormatter = itemDisplayFormatter;
            this.caseItemHelper = caseItemHelper;
            this.caseMediaHelper = caseMediaHelper;
            this.autoDispoHelper = autoDispoHelper;
            this.caseAutoDispoHelper = caseAutoDispoHelper;
            this.translationsHelperBase = translationsHelperBase;
            this.requestHelper = requestHelper;
            this.caseMassUpdate = caseMassUpdate;
            this.tagUpdateHelper = tagUpdateHelper;
            this.mapper = mapper;
            this.formHelper = formHelper;
            this.caseAddAndEditQueueWriter = caseAddAndEditQueueWriter;
            this.logger = logger;
            this.caseMediaCtrlHelper = caseMediaCtrlHelper;
            this.mediaHelper = mediaHelper;
            this.ecWorkflowFactory = ecWorkflowFactory;
            this.ecGenUtils = ecGenUtils;
        }

        #region CRUD 

        /// <summary>
        /// Returns a case
        /// </summary>
        /// <param name="id">id of the case in the organization</param>
        /// <returns>The found case</returns>
        /// <response code="200">Returns the case</response>
        /// <response code="404">The case is not found</response>
        [AccessType(AccessType.List, true)]
        [HttpGet, Route("{id}")]
        public async Task<IActionResult> GetCase(long id)
        {
            if (id <= 0)
            {
                return NotFound();
            }

            var caseVm = await mongoCaseHandler.GetCase(id, SelectedOrganizationID);
            if (caseVm == null)
            {
                return NotFound();
            }

            if (!await HasAdminAccess())
            {
                var hasOfficeAccess = permissionsHelper.HasAccess(ModelType.Cases, AccessType.List, SelectedOrganizationID, caseVm.OfficeId,
                    UserId);
                if (!hasOfficeAccess)
                {
                    //NOTE: this feels redundant but it's necessary
                    caseLevelPermissionsHelper.PopulateBase(SelectedOrganizationID);
                    var clpStatus = await caseLevelPermissionsHelper.GetEntityCLPStatus(AccessType.List, new CLPEntityIdAndType { Id = caseVm.Id, Type = PermissionsEntityType.Case }, UserId);
                    if (clpStatus != EntityCLPStatus.Available)
                    {
                        return new ForbidResult();
                    }
                }
            }
            await caseDisplayFormatter.FormatFields(caseVm, SelectedOrganizationID, UserId, await RestrictMediaToOwner());
            var allUserOffices = await permissionsHelper.AllUserOfficeIds(await HasAdminAccess(), SelectedOrganizationID, UserId);
            await recentCasesHelper.AddRecentCase(caseVm, allUserOffices, UserId);

            if (!string.IsNullOrWhiteSpace(caseVm.Version))
            {
                HttpContext.Response.Headers.Add(HeaderNames.ETag, caseVm.Version.ToETag());
            }
            return Ok(caseVm);
        }

        /// <summary>
        /// Returns the case number for a case 
        /// </summary>
        /// <param name="id">id of the case in the organization</param>
        /// <returns>The found cases case number</returns>
        /// <response code="200">Returns the case number</response>
        /// <response code="404">The case is not found</response>
        [AccessType(AccessType.Any, false)]
        [HttpGet, Route("{id}/caseNumber")]
        public async Task<IActionResult> GetCaseNumber(long id)
        {
            if (id <= 0)
            {
                return NotFound();
            }

            var hasAdminAccess = await HasAdminAccess();
            var allUserOffices = hasAdminAccess
                ? null
                : await permissionsHelper.AllUserOfficeIds(hasAdminAccess, SelectedOrganizationID, UserId);
            var caseNumber = await mongoCaseHandler.GetCaseNumberUI(id, SelectedOrganizationID, hasAdminAccess, allUserOffices);
            if (caseNumber == null)
            {
                return NotFound();
            }
            var returnedCaseNumber = new CaseNumberViewModel
            {
                Id = id,
                CaseNumber = caseNumber
            };
            return Ok(returnedCaseNumber);
        }

        /// <summary>
        /// Updates a case, creating a workflow command after the update
        /// </summary>
        /// <param name="id">id of the case in the organization</param>
        /// <returns>Nothing</returns>
        /// <response code="200">Nothing</response>
        /// <response code="400">Case validation fails</response>
        /// <response code="404">The case is not found</response>
        /// <response code="412">The case is not the current case in db (i.e. old version)</response>
        [AccessType(AccessType.Update, true)]
        [HttpPut, Route("{id}")]
        public async Task<IActionResult> PutCase(long id, CaseViewModel caseToUpdate, bool updateCustomForms = true)
        {
            if (caseToUpdate == null || id != caseToUpdate.Id)
            {
                return BadRequest("Update error: invalid update data".ToError());
            }

            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var languageTemplate = await CurrentLanguageTemplateId();
            var hasAdminAccess = await HasAdminAccess();

            var (result, foundCase) = await caseUpdateBusLogic.ValidateCase(caseToUpdate, SelectedOrganizationID, SelectedOfficeID,
                UserId, hasAdminAccess, languageTemplate);
            if (foundCase == null)
            {
                return result;
            }

            try
            {
                await copyCaseVmToBsonDoc.UpdateCase(foundCase, caseToUpdate, hasAdminAccess, SelectedOrganizationID, SelectedOfficeID,
                    UserId, false, updateCustomForms);
            }
            catch (FormDataInvalidException ex)
            {
                return BadRequest(ex.Message.ToError());
            }

            var ifMatchValue = Request.Headers[HeaderNames.IfMatch].FirstOrDefault()?.Trim('\"');
            var numUpdated = await caseHelper.UpdateCaseWithHistoryMongo(foundCase, SelectedOrganizationID, UserId, ifMatchValue);
            if (numUpdated == 0)
            {
                return StatusCode(StatusCodes.Status412PreconditionFailed);
            }


            caseAddAndEditQueueWriter.QueueCaseUpdateRequest(new CaseUpdatePayload()
            {
                BsonDoc = foundCase.ToJson()
            });
            var allUserOffices = await permissionsHelper.AllUserOfficeIds(hasAdminAccess, SelectedOrganizationID, UserId);
            await recentCasesHelper.AddRecentCase(caseToUpdate, allUserOffices, UserId);

            // Workflow
            workflowQueueWriter.QueueWorkflowRequest(new WorkflowRequest
            {
                UserId = UserId,
                OrganizationId = SelectedOrganizationID,
                Action = WorkflowActionEnum.Updated,
                ModelType = WorkflowTypeEnum.Cases,
                EntityId = id
            });

            HttpContext.Response.Headers.Add(HeaderNames.ETag, foundCase[MongoConstants.Version].AsString.ToETag());
            return Ok();
        }

        /// <summary>
        /// Creates a new case, creating a workflow command after the update
        /// </summary>
        /// <returns>The case that was created</returns>
        /// <response code="200">Returns the case number</response>
        /// <response code="400">Case validation fails</response>
        /// <response code="409">Case number validation fails (i.e. not set)</response>
        [AccessType(AccessType.Create, true)]
        [HttpPost]
        public async Task<IActionResult> PostCase([FromBody] CaseViewModel caseToPost)
        {
            if (caseToPost == null)
            {
                return BadRequest();
            }

            var languageTemplate = await CurrentLanguageTemplateId();
            var hasAdminAccess = await HasAdminAccess();

            newCaseBusinessLogic.SetNewCaseBasicFields(caseToPost, SelectedOrganizationID, SelectedOfficeID, UserId);
            var error = await newCaseBusinessLogic.ValidateCase(caseToPost, languageTemplate);
            if (error != null)
            {
                return error;
            }

            var caseBson = caseToPost.ToBsonDocument();
            try
            {
                await copyCaseVmToBsonDoc.UpdateCase(caseBson, caseToPost, hasAdminAccess, SelectedOrganizationID, SelectedOfficeID,
                    UserId, false, true);
            }
            catch (FormDataInvalidException ex)
            {
                return BadRequest(ex.Message.ToError());
            }

            await caseHelper.AddCaseWithHistoryMongoAndSql(caseBson, UserId);

            logger.LogWarning("Queuing case add request");
            caseAddAndEditQueueWriter.QueueCaseAddRequest(new CaseAddPayload()
            {
                BsonDoc = caseBson.ToJson()
            });
            logger.LogWarning("Done");

            var allUserOffices = await permissionsHelper.AllUserOfficeIds(hasAdminAccess, SelectedOrganizationID, UserId);
            await recentCasesHelper.AddRecentCase(caseToPost, allUserOffices, UserId);

            // Workflow
            workflowQueueWriter.QueueWorkflowRequest(new WorkflowRequest
            {
                UserId = UserId,
                OrganizationId = SelectedOrganizationID,
                Action = WorkflowActionEnum.Created,
                ModelType = WorkflowTypeEnum.Cases,
                EntityId = caseToPost.Id
            });

            return Ok(caseToPost);
        }
        #endregion

        #region Custom Forms

        /// <summary>
        /// Updates the forms of a case
        /// </summary>
        /// <param name="id">id of the case in the organization</param>
        /// <param name="formsToUpdate">The forms to update</param>
        /// <returns></returns>
        /// <response code="200">Returns the case number</response>
        /// <response code="400">Forms to update is null or Form data is invalid (form not found or string length greater than 10k chars)</response>
        /// <response code="404">The case is not found</response>
        /// <response code="412">The case is not the current case in db (i.e. old version)</response>
        [AccessType(AccessType.Update, true)]
        [HttpPut, Route("customforms/{id}")]
        public async Task<IActionResult> PutCaseCustomForms(long id, ICollection<FormDatumCaseMongo> formsToUpdate)
        {
            if (formsToUpdate == null)
            {
                return BadRequest("Update error: invalid update data".ToError());
            }

            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var foundCase = await mongoCaseHandler.GetCaseBsonById(id, SelectedOrganizationID, true);
            if (foundCase == null)
            {
                return NotFound("Case not found".ToError());
            }

            if (foundCase.GetValue(MongoConstants.CaseOfficeId).ToInt64() != SelectedOfficeID)
            {
                return BadRequest("Can not update items from a different office".ToError());
            }

            try
            {
                await copyFormsToBsonDoc.CopyForms(foundCase, id, formsToUpdate.Cast<FormDatumMongo>().ToList(), SelectedOrganizationID, SelectedOfficeID,
                    false);
            }
            catch (FormDataInvalidException ex)
            {
                return BadRequest(ex.Message.ToError());
            }

            var ifMatchValue = Request.Headers[HeaderNames.IfMatch].FirstOrDefault()?.Trim('\"');
            var numUpdated = await caseHelper.UpdateCaseWithHistoryMongo(foundCase, SelectedOrganizationID, UserId, ifMatchValue);
            if (numUpdated == 0)
            {
                return StatusCode(StatusCodes.Status412PreconditionFailed);
            }

            caseAddAndEditQueueWriter.QueueCaseUpdateRequest(new CaseUpdatePayload()
            {
                BsonDoc = foundCase.ToJson()
            });

            workflowQueueWriter.QueueWorkflowRequest(new WorkflowRequest
            {
                UserId = UserId,
                OrganizationId = SelectedOrganizationID,
                Action = WorkflowActionEnum.Updated,
                ModelType = WorkflowTypeEnum.Cases,
                EntityId = id
            });

            HttpContext.Response.Headers.Add(HeaderNames.ETag, foundCase[MongoConstants.Version].AsString.ToETag());
            return Ok();
        }

        /// <summary>
        /// Get custom forms for a specified case id
        /// </summary>
        /// <param name="id">id of the case in the organization</param>
        /// <returns>Custom forms for the case</returns>
        /// <response code="200">Returns the case form data</response>
        /// <response code="404">The case is not found</response>
        [AccessType(AccessType.List, true)]
        [HttpGet, Route("{id}/formdata")]
        public async Task<IActionResult> GetCustomForms(long id)
        {
            if (id <= 0)
            {
                return NotFound();
            }

            var caseBson = await mongoCaseHandler.GetCaseBsonById(id, SelectedOrganizationID, true);
            if (caseBson == null)
            {
                return NotFound();
            }
            var formData = formHelper.GetFormData<FormDatumCaseMongo>(caseBson, id);
            return Ok(formData);
        }


        #endregion

        #region Get Cases List for Item View

        /// <summary>
        /// Get cases that an item is associated with
        /// </summary>
        /// <param name="itemId">id of the item in the organization</param>
        /// <param name="searchParameters">Pagination parameters for the search (orderBy, orderByAsc, pageNumber, pageSize)</param>
        /// <returns>Cases that the item belongs to</returns>
        /// <response code="200">Returns the cases that the item belongs to</response>
        /// <response code="400">Search parameters invalid (max page size is 100)</response>
        /// <response code="404">The item is not found</response>
        [AccessType(AccessType.List, true)]
        [HttpPost, Route("{itemId}/caseslist")]
        public async Task<IActionResult> GetCasesListForItemView(long itemId, [FromBody] SearchViewModelBase searchParameters)
        {
            // Validation
            var errorMessage = searchModelValidation.Validate(searchParameters);
            if (!string.IsNullOrWhiteSpace(errorMessage))
            {
                return BadRequest(errorMessage.ToError());
            }

            var languageTemplate = await CurrentLanguageTemplateId();
            var hasAdminAccess = await HasAdminAccess();

            var (errorResponse, result) = await getCaseListForItemView.Execute(UserId, itemId, SelectedOrganizationID, languageTemplate,
                hasAdminAccess, searchParameters, await RestrictMediaToOwner());
            if (errorResponse != null)
            {
                return errorResponse;
            }
            return Ok(result);
        }

        #endregion

        #region (POST) Return Case by an Item's PrimaryCaseId

        /// <summary>
        /// Get primary case for a given item 
        /// </summary>
        /// <param name="itemId">id of the case in the organization</param>
        /// <returns>Item's primary case</returns>
        /// <response code="200">Returns the item's primary case</response>
        /// <response code="404">The item is not found</response>
        // POST: api/Cases/byitemid
        [AccessType(AccessType.List, true)]
        [HttpPost, Route("byitemid/{itemId}")]
        public async Task<IActionResult> GetCaseByItemPrimaryCaseId(long itemId)
        {
            var item = await mongoItemHandler.GetItemBson(itemId, SelectedOrganizationID);
            if (item == null)
            {
                return NotFound();
            }

            var caseVm = await mongoCaseHandler.GetCase(item[MongoConstants.ItemPrimaryCaseId].AsInt64, SelectedOrganizationID);
            // Why do we do this in the old API?
            var cases = new List<CaseViewModel>() { caseVm };
            // Case-Level Permissions and Office Permissions - for Cases
            var hasAdminAccess = await HasAdminAccess();
            if (!hasAdminAccess)
            {
                caseLevelPermissionsHelper.PopulateBase(SelectedOrganizationID);
                await caseLevelPermissionsHelper.StripInaccessibleEntities(cases, hasAdminAccess, UserId);
            }

            // Formatting
            await caseDisplayFormatter.FormatFields(cases, SelectedOrganizationID, UserId, await RestrictMediaToOwner(), true);
            return Ok(cases);
        }
        #endregion

        #region (POST) Find Cases by List of Case Ids


        /// <summary>
        /// Get cases via array of case ids
        /// </summary>
        /// <param name="ids">array of case ids that exist in the organization</param>
        /// <returns>List of Cases</returns>
        /// <response code="200">Returns the list of cases</response>
        /// <response code="400">ids length > 1k values</response>
        /// <response code="404">Some of the ids were not found</response>
        // GET: api/Cases/bycaseidlist
        [AccessType(AccessType.List, true)]
        [HttpPost, Route("bycaseidlist")]
        public async Task<IActionResult> GetByCaseIdList([FromBody] long[] ids)
        {
            if (ids.Length > MaxCaseIdLength)
            {
                return BadRequest($"Maximum number of cases allowed: {MaxCaseIdLength}".ToError());
            }
            var cases = await mongoCaseHandler.GetCases(ids, SelectedOrganizationID);
            if (cases.Count != ids.Length)
            {
                return NotFound();
            }

            var hasAdminAccess = await HasAdminAccess();
            if (!hasAdminAccess)
            {
                caseLevelPermissionsHelper.PopulateBase(SelectedOrganizationID);
                await caseLevelPermissionsHelper.StripInaccessibleEntities(cases, hasAdminAccess, UserId);
            }
            // Formatting
            await caseDisplayFormatter.FormatFields(cases, SelectedOrganizationID, UserId, await RestrictMediaToOwner(), true);
            return Ok(cases);
        }

        /// <summary>
        /// Get cases via array of case ids
        /// </summary>
        /// <param name="ids">array of case ids that exist in the organization</param>
        /// <returns>List of cases</returns>
        /// <response code="200">Returns the list of cases</response>
        /// <response code="400">ids length > 1k values</response>
        /// <response code="404">Some of the ids were not found</response>
        [AccessType(AccessType.List, true)]
        [HttpPost, Route("byentityidlist")]
        public async Task<IActionResult> GetByCaseIds([FromBody] long[] ids)
        {
            return await GetByCaseIdList(ids);
        }
        #endregion

        #region Typeahead

        /// <summary>
        /// Returns a list of mini-cases (id, case number) for the given search
        /// </summary>
        /// <param name="search">string to search on</param>
        /// <param name="allOffices">To search via all user's offices, or just current office</param>
        /// <param name="excludeAllRestricted">If this is set, then all restricted cases will be removed</param>
        /// <param name="addIsForbidden">If this is set, then all restricted will show up as forbidden</param>
        /// <param name="model">The model type to use for case access</param>
        /// <param name="access">The access type to use for case access</param>
        /// <returns>Returns the list of mini-cases</returns>
        /// <response code="200">Returns the list of mini-cases</response>
        /// <response code="400">ids length > 1k values</response>
        /// <response code="404">Some of the ids were not found</response>
        // GET: api/Cases/typeahead
        [AccessType(AccessType.Any)]
        [HttpGet, Route("typeahead")]
        public async Task<IActionResult> GetCasesTypeahead(
            string search,
            bool allOffices = false,
            bool excludeAllRestricted = false,
            bool addIsForbidden = false,
            ModelType model = ModelType.Cases,
            AccessType access = AccessType.List)
        {
            search = !string.IsNullOrWhiteSpace(search) ? search.ToLower() : string.Empty;
            var hasAdminAccess = await HasAdminAccess();
            var officeIds = allOffices
                ? await permissionsHelper.AllUserOfficeIds(hasAdminAccess, SelectedOrganizationID, UserId)
                : new long[] { SelectedOfficeID };

            var caseListTypeahead = await caseHelper.GetCasesTypeahead(search, officeIds, SearchConstants.TypeaheadResultsCountLimit);
            // Remove Ids (or set isForbidden to true) of the cases user have no access to
            // or remove all restricted if needed (when excludeAllRestricted = true)
            if (!hasAdminAccess || excludeAllRestricted)
            {
                await caseHelper.ProcessInaccessibleCases(
                    caseListTypeahead.Cases,
                    UserId,
                    SelectedOrganizationID,
                    excludeAllRestricted,
                    addIsForbidden,
                    model,
                    access);
            }

            return Ok(caseListTypeahead);
        }

        /// <summary>
        /// Gets the first case list model (id, case-number) that matches (even partially) the search string
        /// </summary>
        /// <param name="search">array of case ids that exist in the organization</param>
        /// <returns>The one case list model</returns>
        /// <response code="200">Returns the one case list model</response>
        // GET: api/Cases/typeaheadGetOne
        [AccessType(AccessType.List, true)]
        [HttpGet, Route("typeaheadGetOne")]
        public async Task<IActionResult> TypeaheadGetOne(string search)
        {
            var result = await caseHelper.TypeaheadGetOne(search, SelectedOfficeID);
            return Ok(result);
        }

        #endregion

        #region History

        /// <summary>
        /// Get cases history that matches the search parameters
        /// </summary>
        /// <remarks>
        /// Sample request:
        ///
        ///     POST 1/history
        ///     {
        ///        "searchString": "string",
        ///        "orderBy": "PrimaryCaseNumber",
        ///        "orderByAscending": true,
        ///        "pageNumber": 1,
        ///        "pageSize": 100
        ///     }
        ///
        /// </remarks>
        /// <param name="id">Id of the case to get history for</param>
        /// <param name="searchParameters">Paging parameters as well as search string to perform history search on</param>
        /// <returns>List of case histories</returns>
        /// <response code="200">Returns the list of case histories</response>
        /// <response code="400">Paging parameters are invalid (less than or equal to 0 or greater than 500)</response>
        [AccessType(AccessType.List, true)]
        [HttpPost, Route("{id}/history")]
        public async Task<IActionResult> GetHistory(long id, [FromBody] TabSearchViewModel searchParameters)
        {
            // Validation
            var errorMessage = searchModelValidation.Validate(searchParameters);
            if (!string.IsNullOrWhiteSpace(errorMessage))
            {
                return BadRequest(errorMessage.ToError());
            }
            var historiesResponse = await caseHistoryHelper.SearchCaseHistoriesTab(id, SelectedOrganizationID, searchParameters);
            return Ok(historiesResponse);
        }

        /// <summary>
        /// Retrieve case history and previous history by id
        /// </summary>
        /// <param name="caseId">The case id</param>
        /// <param name="historyId">The history id of the case</param>
        /// <returns>Case history and previous</returns>
        /// <response code="200">Case history and previous</response>
        [AccessType(AccessType.List, true, "caseId")]
        [HttpGet, Route("{caseId}/history/{historyId}")]
        public async Task<IActionResult> GetCaseHistory(long caseId, string historyId)
        {
            var response = await caseHistoryHelper.GetCaseLatestHistoryAndPrevious(caseId, historyId, SelectedOrganizationID);
            return Ok(response);
        }

        /// <summary>
        /// Searching for case history
        /// </summary>
        /// <param name="model">The model to perform the search on (from body)</param>
        /// <returns>List of case history models</returns>
        /// <response code="200">Returns list of case history models</response>
        /// <response code="400">Invalid model (paging params out of range, model is null, model doesn't contain offices)</response>
        // POST: api/cases/historySearch
        [HttpPost, Route("historySearch")]
        public async Task<IActionResult> HistorySearch([FromBody] SearchViewModel model)
        {
            // Validation
            var errorMessage = await searchModelValidation.Validate(model, UserId, SelectedOrganizationID, await CurrentLanguageTemplateId());
            if (!string.IsNullOrWhiteSpace(errorMessage))
            {
                return BadRequest(errorMessage.ToError());
            }

            var (histories, total) = await mongoSearch.PerformSearch<CaseHistoryMongo>(model, MongoConstants.CaseHistoryCollection,
                historyQueryBuilder, SelectedOrganizationID, MongoConstants.CaseHistoryCase, true);

            // Case-Level Permissions and Office Permissions - for Cases
            var hasAdminAccess = await HasAdminAccess();
            if (!hasAdminAccess)
            {
                var foundCases = histories.Select(x => x.Case).ToList();
                caseLevelPermissionsHelper.PopulateBase(SelectedOrganizationID);
                await caseLevelPermissionsHelper.StripInaccessibleEntities(foundCases, hasAdminAccess, UserId);
            }

            var historiesForUI = caseHistoryHelper.FormatCaseHistorySearchResponse(histories, total, SearchConstants.SearchResultsLimit);
            return Ok(historiesForUI);
        }

        #endregion

        #region Search

        /// <summary>
        /// Searching for cases
        /// </summary>
        /// <param name="model">The search params to perform search on (from body)</param>
        /// <returns>List of cases</returns>
        /// <response code="200">Returns list of cases</response>
        /// <response code="400">Invalid model (paging params out of range, model is null, model doesn't contain offices)</response>
        // POST: api/cases/search
        [HttpPost, Route("search")]
        public async Task<IActionResult> CustomSearch(SearchViewModel model)
        {            
            var guid = string.Empty;
            if (HttpContext.Request.Headers.TryGetValue("guid", out var value))
            {
                guid = value.First();                
            }
            var startTime = DateTime.UtcNow;
            var stopWatchValidateCase = new Stopwatch();
            var swTotal = new Stopwatch();
            stopWatchValidateCase.Start();
            swTotal.Start();

            // Validation
            var errorMessage = await searchModelValidation.Validate(model, UserId, SelectedOrganizationID, await CurrentLanguageTemplateId());
            if (!string.IsNullOrWhiteSpace(errorMessage))
            {
                return BadRequest(errorMessage.ToError());
            }
            stopWatchValidateCase.Stop();
            var stopWatchSearch = new Stopwatch();
            stopWatchSearch.Start();

            long total;
            List<CaseViewModelList> foundCases;
            if (esConfig.UseElasticSearch)
            {
                CasesEsQueryBuilder.Logger = x => logger.LogWarning(x);
                (foundCases, total) =  caseEsQueryBuilder.PerformSearch(model, SelectedOrganizationID);                
            }
            else
            {
                (foundCases, total) = await mongoSearch.PerformSearch<CaseViewModelList>(model, MongoConstants.CasesCollection,
                        caseQueryBuilder, SelectedOrganizationID);
            }
            stopWatchSearch.Stop();
            var stopWatchClp = new Stopwatch();
            stopWatchClp.Start();

            // Case-Level Permissions and Office Permissions - for Cases
            var hasAdminAccess = await HasAdminAccess();
            if (!hasAdminAccess)
            {
                caseLevelPermissionsHelper.PopulateBase(SelectedOrganizationID);
                await caseLevelPermissionsHelper.StripInaccessibleEntities(foundCases, hasAdminAccess, UserId);
            }
            stopWatchClp.Stop();
            var stopWatchFormatting = new Stopwatch();
            stopWatchFormatting.Start();

            await caseDisplayFormatter.FormatFields(foundCases.Cast<CaseViewModel>().ToList(), SelectedOrganizationID, UserId, await RestrictMediaToOwner(),
                true);
            stopWatchFormatting.Stop();                       

            if (swTotal.Elapsed.TotalSeconds > 9)
            {
                logger.LogWarning($"******************************");
                logger.LogWarning($"Cases - Guid: {guid} Time Reached: {startTime.ToString("HH:mm:ss.fff")}");
                logger.LogWarning($"Validate Case : {stopWatchValidateCase.ElapsedMilliseconds} ms");
                logger.LogWarning($"Do Case Search : {stopWatchSearch.ElapsedMilliseconds} ms");
                logger.LogWarning($"CLP Cases: {stopWatchClp.ElapsedMilliseconds} ms");
                logger.LogWarning($"Format Fields Cases : {stopWatchFormatting.ElapsedMilliseconds} ms");
                logger.LogWarning($"Cases Guid {guid} Total Search: {swTotal.ElapsedMilliseconds} ms");
            }

            return Ok(new Core.Helpers.GeneralViewModels.SearchResponse<CaseViewModelList>()
            {
                Data = foundCases,
                Count = total,
                LimitExceeded = total >= SearchConstants.SearchResultsLimit
            });
        }

        #endregion

        #region Exist Check

        /// <summary>
        /// Determine if a case number exists
        /// </summary>
        /// <param name="id">The id of the current case</param>
        /// <param name="value">The case number to search for</param>
        /// <returns>If case number exists</returns>
        /// <response code="200">If case number exists</response>
        // GET: api/Cases/exist/casenumber
        [AccessType(AccessType.Any, false)]
        [HttpGet, Route("exists")]
        public async Task<IActionResult> GetExistCaseNumber(long id, string value)
        {
            var exists = await mongoCaseHandler.CaseNumberExists(value, SelectedOrganizationID, id);
            return Ok(exists);
        }

        #endregion

        #region Get Case By Number

        /// <summary>
        /// Retrieve a case by case number
        /// </summary>
        /// <param name="value">The case number to search for</param>
        /// <returns>Case model if it exists else null</returns>
        /// <response code="200">Case model if it exists else null</response>
        // GET: api/Cases/getByNumber/casenumber
        [AccessType(AccessType.List, true)]
        [HttpGet, Route("getByNumber")]
        public async Task<IActionResult> GetCaseByNumber(string value)
        {
            CaseViewModel foundCase = null;

            if (!string.IsNullOrWhiteSpace(value))
            {
                foundCase = await mongoCaseHandler.TryGetCaseVMByCaseNumber(value, SelectedOrganizationID);
                // Case-Level Permissions and Office Permissions - for Cases
                var hasAdminAccess = await HasAdminAccess();
                if (foundCase != null && !hasAdminAccess)
                {
                    caseLevelPermissionsHelper.PopulateBase(SelectedOrganizationID);
                    await caseLevelPermissionsHelper.StripInaccessibleEntities(new List<CaseViewModel> { foundCase }, hasAdminAccess, UserId);
                }
            }

            return Ok(foundCase);
        }

        #endregion

        #region Case People

        /// <summary>
        /// Retrieve count of people involved in a specific case
        /// </summary>
        /// <param name="id">The case id</param>
        /// <returns>Count of people in the case</returns>
        /// <response code="200">Count of people in the case</response>
        // GET: api/cases/5/people
        [AccessType(AccessType.List, true)]
        [HttpGet, Route("{id}/peopleCount")]
        public async Task<IActionResult> GetCasePeopleCount(long id)
        {
            var caseBson = await mongoCaseHandler.GetCaseBsonById(id, SelectedOrganizationID, true);
            if (caseBson == null)
            {
                return NotFound();
            }

            return Ok(await db.CasePersons.Where(e => e.CaseId == id).CountAsync());
        }

        /// <summary>
        /// Get Person id's that belong to a case
        /// </summary>
        /// <param name="id">Case Id</param>
        /// <param name="peopleIds">People Ids to search</param>
        /// <response code="404">Case not found</response>
        /// <returns>List of people ids</returns>
        // POST: api/cases/5/filterPeopleIdsByCaseId
        [AccessType(AccessType.List, true)]
        [HttpPost, Route("{id}/filterPeopleIdsByCaseId")]
        public async Task<IActionResult> FilterPeopleIdsByCaseId(long id, [FromBody] long[] peopleIds)
        {
            var caseBson = await mongoCaseHandler.GetCaseBsonById(id, SelectedOrganizationID, true, false);
            if (caseBson == null)
            {
                return NotFound();
            }

            var filteredPeopleIds = await db.CasePeople
                                            .Where(cp => cp.CaseId == id &&
                                                         peopleIds.Contains(cp.PersonId))
                                            .Select(cp => cp.PersonId)
                                            .ToListAsync();

            return Ok(filteredPeopleIds);
        }

        /// <summary>
        /// Retrieve list of people in a case
        /// </summary>
        /// <remarks>
        /// NOTE: This endpoint doesn't use pagination and should be avoided
        /// </remarks>
        /// <param name="id">The case id</param>
        /// <param name="showInactive">Retrieve inactive people</param>
        /// <returns>People in the case</returns>
        /// <response code="200">People in the case</response>
        // GET: api/cases/5/peopleList
        [Obsolete("Use 'api/cases/{id}/peopleListFiltered' endpoint instead")]
        [AccessType(AccessType.List, true)]
        [HttpGet, Route("{id}/peopleList")]
        public async Task<IActionResult> GetCasePeopleList(long id, bool showInactive = true)
        {
            var caseBson = await mongoCaseHandler.GetCaseBsonById(id, SelectedOrganizationID, true);
            if (caseBson == null)
            {
                return NotFound();
            }

            var results = await caseHelper
                .GetCasePeopleListFiltered(id, SelectedOrganizationID, string.Empty, true, SearchConstants.MaxPageSize, 0, string.Empty, showInactive);

            return Ok(results.Entities);
        }

        /// <summary>
        /// Retrieve paginated list of people in a case
        /// </summary>
        /// <param name="id">The case id</param>
        /// <param name="orderBy">The column to order by</param>
        /// <param name="orderMethodAsc">Order ascending or descending (boolean)</param>
        /// <param name="top">The number of people to return</param>
        /// <param name="skip">Pagination, the number of people to skip</param>
        /// <param name="searchStr">Search string</param>
        /// <param name="showInactive">Search 'active' people only (default true)</param>
        /// <returns>Paginated list of people in the case</returns>
        /// <response code="200">Paginated list of Count of people in the case</response>
        /// <response code="400">Top is greater than max page size which is 500</response>
        // GET: api/cases/5/peopleListFiltered
        [AccessType(AccessType.List, true)]
        [HttpGet, Route("{id}/peopleListFiltered")]
        public async Task<IActionResult> GetCasePeopleListFiltered(
            long id, string orderBy, bool orderMethodAsc, int top, int skip, string searchStr, bool showInactive = true)
        {
            if (top > SearchConstants.MaxPageSize)
            {
                return BadRequest($"Top is larger than max page size which is {SearchConstants.MaxPageSize}".ToError());
            }

            var results = await caseHelper
                .GetCasePeopleListFiltered(id, SelectedOrganizationID, orderBy, orderMethodAsc, top, skip, searchStr, showInactive);

            return Ok(new { Count = results.TotalCount, Data = results.Entities });
        }

        /// <summary>
        /// Add a person to a case
        /// </summary>
        /// <param name="id">The case id</param>
        /// <param name="casePersonVm">The person to add to the case</param>
        /// <returns>The person added to the case</returns>
        /// <response code="200">The person added to the case</response>
        /// <response code="400">If the case is not in current office, or the person is already associated with the case</response>
        /// <response code="404">The case was not found</response>
        // POST: api/cases/5/people
        [AccessType(AccessType.Update, true)]
        [HttpPost, Route("{id}/people")]
        public async Task<IActionResult> PostCasePerson(long id, CasePersonViewModel casePersonVm)
        {
            var isCaseInCurrentOffice = await caseHelper.IsCaseInCurrentOffice(id, SelectedOfficeID);
            if (!isCaseInCurrentOffice.HasValue)
            {
                return NotFound($"Case id {id} not found".ToError());
            }
            if (!isCaseInCurrentOffice.Value)
            {
                return BadRequest("Can not update items from a different office".ToError());
            }
            if (await db.CasePersons.AnyAsync(x => x.CaseId == id && x.PersonId == casePersonVm.PersonId))
            {
                return BadRequest("Person is already associated with the case".ToError());
            }

            var casePerson = mapper.Map<CasePerson>(casePersonVm);
            casePerson.CaseId = id;
            casePerson.OfficeId = SelectedOfficeID;
            casePerson.VisibilityId = await dataVisibilityHelper.GetDatavisibilityId(DataVisibility.Public);

            db.CasePersons.Add(casePerson);
            await db.SaveChangesAsync();

            return Ok(casePerson);
        }

        /// <summary>
        /// Removes a person from a case
        /// </summary>
        /// <param name="id">The case id</param>
        /// <param name="personId">The person id to remove from the case</param>
        /// <returns>The person removed from the case</returns>
        /// <response code="200">The person removed from the case</response>
        /// <response code="400">If the case is not in current office</response>
        /// <response code="404">If the person is not associated with the case or the case is not found</response>
        // DELETE: api/cases/5/people/1
        [AccessType(AccessType.Update, true)]
        [HttpDelete, Route("{id}/people/{personId}")]
        public async Task<IActionResult> DeleteCasePerson(long id, long personId)
        {
            var caseVm = await mongoCaseHandler.GetCase(id, SelectedOrganizationID);
            if (caseVm == null)
            {
                return NotFound("Case not found".ToError());
            }

            var casePersons = await db.CasePersons.Where(i => i.CaseId == id && i.PersonId == personId).ToListAsync();
            if (casePersons.Count == 0)
            {
                return NotFound("Person not found".ToError());
            }

            db.CasePersons.RemoveRange(casePersons);
            await db.SaveChangesAsync();

            return Ok(casePersons);
        }

        #endregion

        #region Get Items - No Paginations

        // Do not use this method anywhere you may need pagination, use the new method below.

        /// <summary>
        /// Returns all items having the given case id as primary case
        /// </summary>
        /// <remarks>
        /// NOTE: This endpoint does not use pagination.  It will be phased out soon.
        /// </remarks>
        /// <param name="id">The case id</param>
        /// <param name="search">The search word</param>
        /// <returns>The items having the given case id as primary case</returns>
        /// <response code="200">The items having the given case id as primary case</response>
        /// <response code="404">The given case id is less or equals 0</response>
        // GET: api/cases/id/primaryCaseItems?search=itemDescr
        [HttpGet, Route("{id}/primaryCaseItems")]
        [AccessType(AccessType.List, true)]
        public async Task<IActionResult> GetPrimaryCaseItems(long id, string search)
        {
            if (id <= 0)
            {
                return NotFound();
            }

            var minifiedItems = await caseItemHelper
                .GetPrimaryCaseItems(search, id, SelectedOrganizationID, await CurrentLanguageTemplateId());

            return Ok(minifiedItems);
        }

        #endregion

        #region Get Items W/ Pagination (e.g. for Case view page - Items tab)

        /// <summary>
        /// Returns a paginated list of all items associated with the given case
        /// </summary>
        /// <param name="id">The case id</param>
        /// <param name="searchParameters">The search parameters</param>
        /// <param name="includePeople">Include item's people in the response</param>
        /// <returns>A paginated list of all items associated with the given case</returns>
        /// <response code="200">A paginated list of all items associated with the given case</response>
        /// <response code="400">If pagination parameters are out of range (pageNumber or pageSize less than or equal to 0 or pageSize greater than 500)</response>
        // POST: api/cases/id/items
        [HttpPost, Route("{id}/items")]
        [AccessType(AccessType.List, true)]
        public async Task<IActionResult> GetCasesItems(long id, [FromBody] SearchViewModelBase searchParameters, bool includePeople = false)
        {
            if (searchParameters.PageNumber <= 0 || searchParameters.PageSize <= 0 || searchParameters.PageSize > SearchConstants.MaxPageSize)
            {
                return BadRequest("Paging parameters are out of range.".ToError());
            }

            var foundItems = await caseItemHelper.GetCaseItemsPaginated(id, SelectedOrganizationID, searchParameters, SearchConstants.SearchResultsLimit);

            // Case-Level Permissions and Office Permissions - for Items
            if (!await HasAdminAccess())
            {
                caseLevelPermissionsHelper.PopulateBase(SelectedOrganizationID);
                await caseLevelPermissionsHelper.StripInaccessibleEntities(foundItems.Entities, false, UserId, id);
            }

            await itemDisplayFormatter.FormatFields(foundItems.Entities.Cast<ItemViewModel>().ToList(), includePeople, SelectedOrganizationID, UserId,
                await RestrictMediaToOwner());
            return Ok(foundItems);
        }

        #endregion

        #region Return Items for a Case, but Only if the User Selected Them

        // This is a convoluted method. The report builder calls it for item reports - we need to display each item the user selected
        // under the appropriate case, but avoid item duplication or showing items that belong to the case but the user didn't select.        

        /// <summary>
        /// Returns items associated to a case from the provided array of userSelectedItemIds
        /// </summary>
        /// <param name="id">The case id</param>
        /// <param name="userSelectedItemIds">The items to return</param>
        /// <returns>Items associated with the case</returns>
        /// <response code="200">Items associated with the case</response>
        /// <response code="400">If userSelectedItemIds length is greater than 1k</response>
        // POST: api/cases/id/itemsinarray
        [AccessType(AccessType.List, true)]
        [HttpPost, Route("{id}/itemsInArray")]
        public async Task<IActionResult> PostItemsInArray(long id, [FromBody] long[] userSelectedItemIds)
        {
            if (userSelectedItemIds.Length > MaxUserSelectedItemIds)
            {
                return BadRequest($"Maximum array length {MaxUserSelectedItemIds}".ToError());
            }
            var items = await caseItemHelper.GetPrimaryCaseItems(id, userSelectedItemIds, SelectedOrganizationID);
            // Case-Level Permissions and Office Permissions - for Items
            if (!await HasAdminAccess())
            {
                caseLevelPermissionsHelper.PopulateBase(SelectedOrganizationID);
                await caseLevelPermissionsHelper.StripInaccessibleEntities(items, false, UserId, id);
            }

            // Formatting
            await itemDisplayFormatter.FormatFields(items, false, SelectedOrganizationID, UserId, await RestrictMediaToOwner());
            return Ok(items);
        }

        #endregion

        #region Media

        /// <summary>
        /// Returns media from folderId, and total count of media from folderIds
        /// </summary>
        /// <param name="id">The case id</param>
        /// <param name="folderId">The folder in which to retrieve media from</param>
        /// <param name="folderIds">The folders to include in the count</param>
        /// <returns>Media and mediaCountWithChildren</returns>
        /// <response code="200">Media and mediaCountWithChildren</response>
        /// <response code="404">If the case was not found</response>
        // POST: api/cases/5/media?folderId=1
        [AccessType(AccessType.List, true)]
        [HttpPost, Route("{id:int}/media")]
        public async Task<IActionResult> GetMedia(long id, long folderId, [FromBody] List<long> folderIds)
        {
            caseLevelPermissionsHelper.PopulateBase(SelectedOrganizationID);
            var entityCLPStatus = await caseLevelPermissionsHelper.GetEntityCLPStatus(AccessType.List, new CLPEntityIdAndType(id, PermissionsEntityType.Case), UserId);
            var hasAdminAccess = await HasAdminAccess();
            bool caseFound;
            List<Medium> media;
            long mediaCountWithChildren;
             
            if (entityCLPStatus == EntityCLPStatus.None || hasAdminAccess)
            {
                // Case is not restricted - use office perms
                (caseFound, media, mediaCountWithChildren) = await caseMediaHelper.GetMedia(id, folderId, folderIds, UserId,
                       await HasThumbnailAccess(), await RestrictThumbnailToOwner(), await RestrictMediaToOwner(), SelectedOrganizationID, await CurrentLanguageTemplateId());

                if (!caseFound)
                {
                    return NotFound("Case not found".ToError());
                }
            }
            else
            {
                // Check List and IfOwner Access for thumbnails and media
                var hasCLPThumbnailAccess = caseLevelPermissionsHelper.HasMediaThumbnailCLPAccess(id, AccessType.List, UserId, PermissionsEntityType.Case);
                var clpRestrictThumbnailToOwner = caseLevelPermissionsHelper.HasMediaThumbnailCLPIfOwnerAccess(id, AccessType.IfOwner, UserId, PermissionsEntityType.Case);
                var clpRestrictMediaToOwner = caseLevelPermissionsHelper.HasIfOwnerMediaCLPAccess(id, ModelType.Media, AccessType.IfOwner, UserId, PermissionsEntityType.Case);

                // Case CLP availability already checked by Access attribute
                (caseFound, media, mediaCountWithChildren) = await caseMediaHelper.GetMedia(id, folderId, folderIds, UserId,
                    hasCLPThumbnailAccess, clpRestrictThumbnailToOwner, false, SelectedOrganizationID, await CurrentLanguageTemplateId());

                if (!caseFound)
                {
                    return NotFound("Case not found".ToError());
                }

                // Case-Level Permissions and Office/IfOwner Permissions - for Media
                if (!hasAdminAccess)
                {
                    await caseLevelPermissionsHelper.StripInaccessibleEntities(media, false, UserId, id, true, clpRestrictMediaToOwner);
                }
            }



            return Ok(new { Media = media, MediaCountWithChildren = mediaCountWithChildren });
        }

        /// <summary>
        /// Returns media count for case folder id
        /// </summary>
        /// <param name="id">The case id</param>
        /// <param name="folderId">The folder in which to retrieve media count from</param>
        /// <returns>Count of media inside of the folder</returns>
        /// <response code="200">Count of media inside of the folder</response>
        /// <response code="404">If the case was not found</response>
        // GET: api/cases/5/mediaAmount?folderId=1
        [AccessType(AccessType.List, true)]
        [HttpGet, Route("{id:int}/mediaAmount")]
        public async Task<IActionResult> GetMediaAmount(long id, long folderId)
        {
            var (caseFound, mediaAmount) = await caseMediaHelper.GetMediaCount(id, folderId, UserId, SelectedOrganizationID, await RestrictMediaToOwner());
            if (!caseFound)
            {
                return NotFound("Case not found".ToError());
            }
            return Ok(mediaAmount);
        }

        /// <summary>
        /// Returns media count for items associated with a case
        /// </summary>
        /// <param name="id">The case id</param>
        /// <returns>Count of media of items associated with the case</returns>
        /// <response code="200">Count of media of items associated with the case</response>
        /// <response code="404">If the case was not found</response>
        // GET: api/cases/5/itemsMediaAmount
        [AccessType(AccessType.List, true)]
        [HttpGet, Route("{id:int}/itemsMediaAmount")]
        public async Task<IActionResult> GetItemsMediaAmount(long id)
        {
            var (foundCase, mediaCount, limitExceeded) = await caseMediaHelper.GetItemsMediaAmount(id, SelectedOrganizationID, UserId,
                await CurrentLanguageTemplateId(), await RestrictMediaToOwner());
            if (!foundCase)
            {
                return NotFound("Case not found".ToError());
            }
            return Ok(new { Count = mediaCount, CountLimitExceeded = limitExceeded });
        }

        /// <summary>
        /// Returns media sub folders for the given case id
        /// </summary>
        /// <param name="id">The case id</param>
        /// <returns>Media sub folders and count for the case</returns>
        /// <response code="200">Media sub folders and count for the case</response>
        /// <response code="404">If the case was not found</response>
        // GET: api/cases/5/getMediaSubfoldersById
        [AccessType(AccessType.List, true)]
        [HttpGet, Route("{id:int}/getMediaSubfoldersById")]
        public async Task<IActionResult> GetMediaSubfoldersById(long id)
        {
            var (caseFound, foundSubFolders) = await caseMediaHelper.GetMediaSubfoldersByCaseId(id, UserId, SelectedOrganizationID, await RestrictMediaToOwner());
            if (!caseFound)
            {
                return NotFound("Case not found".ToError());
            }
            return Ok(new { SubFolders = foundSubFolders, foundSubFolders.Count });
        }

        /// <summary>
        /// Returns media sub folders for the given case by case number
        /// </summary>
        /// <param name="caseNumber">The case number</param>
        /// <returns>Media sub folders and count for the case</returns>
        /// <response code="200">Media sub folders and count for the case</response>
        /// <response code="404">If the case was not found</response>
        // GET: api/cases/getMediaSubfoldersByCaseNumber/2018-0724-01
        [AccessType(AccessType.List, true)]
        [HttpGet, Route("getMediaSubfoldersByCaseNumber/{caseNumber}")]
        public async Task<IActionResult> GetMediaSubfoldersByCaseNumber(string caseNumber)
        {
            var (caseFound, foundSubFolders) =
                await caseMediaHelper.GetMediaSubfoldersByCaseNumber(caseNumber, UserId, SelectedOrganizationID, await RestrictMediaToOwner(), await HasAdminAccess());

            if (!caseFound)
            {
                return NotFound("Case not found".ToError());
            }
            return Ok(new { SubFolders = foundSubFolders, foundSubFolders.Count });
        }

        #endregion

        #region Notes

        // See Notes Controller
        // This is a generic controller with an instance of NoteCases that exends this route

        /// <summary>
        /// Returns notes count for items associated to the case
        /// </summary>
        /// <param name="id">The case id</param>
        /// <returns>Notes count for items associated to the case</returns>
        /// <response code="200">Notes count for items associated to the case</response>
        /// <response code="404">If the case was not found</response>
        // GET: api/cases/5/itemsNotesCount
        [AccessType(AccessType.List, true)]
        [HttpGet, Route("{id:int}/itemsNotesCount")]
        public async Task<IActionResult> GetItemsNotesCount(long id)
        {
            var (foundCase, itemsNotesCount) = await caseItemHelper.GetItemsNotesCount(id, SelectedOrganizationID);
            if (!foundCase)
            {
                return NotFound("Case not found".ToError());
            }
            return Ok(new { Count = itemsNotesCount });
        }

        #endregion

        #region Tasks

        /// <summary>
        /// Returns task count for a given case id
        /// </summary>
        /// <param name="id">The case id</param>
        /// <returns>Task count for a give case</returns>
        /// <response code="200">Task count for a give case</response>
        // GET: api/cases/5/tasksCount
        [AccessType(AccessType.List, true)]
        [HttpGet, Route("{id}/tasksCount")]
        public async Task<IActionResult> GetCaseTasksCount(long id)
        {
            var tasksCount = await caseHelper.GetCaseTasksCount(id, SelectedOrganizationID);
            return Ok(tasksCount);
        }

        /// <summary>
        /// Returns task count for items associated to a the case
        /// </summary>
        /// <param name="id">The case id</param>
        /// <returns>Task count for items associated to the case</returns>
        /// <response code="200">Task count for items associated to the case</response>
        /// <response code="404">The case not found</response>
        // GET: api/cases/5/itemsTasksCount
        [AccessType(AccessType.List, true)]
        [HttpGet, Route("{id}/itemsTasksCount")]
        public async Task<IActionResult> GetItemsTasksCount(long id)
        {
            var (foundCase, tasksCount) = await caseHelper.GetItemsTasksCount(id, SelectedOrganizationID);
            if (!foundCase)
            {
                return NotFound("Case not found".ToError());
            }
            return Ok(new { Count = tasksCount });
        }

        /// <summary>
        /// Returns a paginated list of tasks associated with the case
        /// </summary>
        /// <param name="id">The case id</param>
        /// <param name="request">The pagination parameters for the case (pageNumber, etc)</param>
        /// <returns>A paginated list of tasks associated with the case</returns>
        /// <response code="200">A paginated list of tasks associated with the case</response>
        /// <response code="404">The case not found</response>
        // POST: api/cases/5/tasks
        [AccessType(AccessType.List, true)]
        [HttpPost, Route("{id:int}/tasks")]
        public async Task<IActionResult> GetTasks(long id, TasksListRequest request)
        {
            var (foundCase, response) = await caseHelper.GetTasks(id, request, UserId, SelectedOrganizationID);

            if (!foundCase)
            {
                return NotFound("Case not found".ToError());
            }

            if (!await HasAdminAccess())
            {
                caseLevelPermissionsHelper.PopulateBase(SelectedOrganizationID);
                await caseLevelPermissionsHelper.StripInaccessibleEntities(response.Tasks, false, UserId, id);
            }

            return Ok(response);
        }

        /// <summary>
        /// Returns a list of mini-cases in an array of ids
        /// </summary>
        /// <param name="ids">The case ids to search for</param>
        /// <returns>A list of mini-cases from the ids list</returns>
        /// <response code="200">A list of mini-cases from the ids list</response>
        /// <response code="400">ids array is invalid (null, or 0 length, or greater than 1k)</response>
        /// <response code="404">Some of the cases were not found</response>
        [AccessType(AccessType.List, true)] // Needs to be checked and changed
        [HttpPost, Route("mini-cases")]
        public async Task<IActionResult> GetMiniCases(long[] ids)
        {
            if (ids == null || !ids.Any() || ids.Count() > MaxMiniCasesLength)
            {
                return BadRequest();
            }

            var minCases = await mongoCaseHandler.GetCaseListVmCases(ids, SelectedOrganizationID);
            if (minCases.Count != ids.Count())
            {
                return NotFound();
            }

            // We are doing a check here to prevent user from gaining access to cases by changing url
            if (!await HasAdminAccess())
            {
                // Exclude Current Office from the check
                var officeIds = minCases.Select(i => i.OfficeId).Where(i => i != SelectedOfficeID)
                    .GroupBy(x => x)
                    .Select(x => x.First())
                    .ToList();
                var hasOfficeAccess = permissionsHelper.HasAccessMany(
                    ModelType.Cases,
                    AccessType.List,
                    SelectedOrganizationID,
                    UserId,
                    officeIds);

                if (!hasOfficeAccess)
                {
                    return Forbid();
                    //return new StatusCodeResult(HttpStatusCode.Forbidden, this.Request);
                }

                // Case-Level Permissions and Office Permissions - for Cases
                caseLevelPermissionsHelper.PopulateBase(SelectedOrganizationID);
                await caseLevelPermissionsHelper.StripInaccessibleEntities(minCases, false, UserId);
            }
            return Ok(minCases);
        }

        #endregion

        #region Check Case Follow-up Updates

        /// <summary>
        /// This function is currently broken and should not be used (broke on old api)
        /// </summary>
        /// <returns></returns>
        // GET: api/Cases/checkcasefollowupdates
        [AccessType(AccessType.SystemAdmin)]
        [HttpGet, Route("checkcasefollowupdates")]
        public async Task<IActionResult> CheckCaseFollowUpDates()
        {
            await db.Database.ExecuteSqlRawAsync("EXEC CreateAssignmentsForCasesUpForReview");
            return Ok();
        }

        #endregion

        #region Case Number Configuration

        /// <summary>
        /// Returns the case number configuration for the selected organization and office
        /// </summary>
        /// <returns>The case number configuration for the selected organization and office</returns>
        /// <response code="200">The case number configuration for the selected organization and office</response>
        // GET: api/cases/caseNumberConfiguration
        [AccessType(AccessType.Any, true)]
        [HttpGet, Route("caseNumberConfiguration")]
        public async Task<IActionResult> GetCaseNumberConfiguration()
        {
            var organization = await db.Organizations
                                       .Include(o => o.CaseNumberConfiguration)
                                       .Include(o => o.CaseNumberOfficeConfigurations)
                                       .SingleAsync(o => o.Id == SelectedOrganizationID);

            var caseNumberConfiguration = organization.CaseNumberConfiguration;
            caseNumberConfiguration.ExamplePattern = caseNumberConfiguration?.IsFormattingRequired == true && 
                                                     !String.IsNullOrEmpty(caseNumberConfiguration?.FormattingPattern)
                ? new Regex(@caseNumberConfiguration.FormattingPattern).GetExamplePattern(true)
                : string.Empty;
            var caseNumberOfficeConfigurations = organization.CaseNumberOfficeConfigurations == null
                ? new List<CaseNumberOfficeConfiguration>()
                : organization.CaseNumberOfficeConfigurations.ToList();

            foreach(var officeConfiguration in caseNumberOfficeConfigurations)
            {
                officeConfiguration.ExamplePattern = caseNumberConfiguration?.IsFormattingRequired == true &&
                                                     !String.IsNullOrEmpty(officeConfiguration.FormattingPattern)
                    ? new Regex(@officeConfiguration.FormattingPattern).GetExamplePattern(true)
                    : string.Empty;
            }

            return Ok(new { caseNumberConfiguration = caseNumberConfiguration, caseNumberOfficeConfigurations = caseNumberOfficeConfigurations});
        }

        #endregion

        #region Case Review Date Statistic and Mass Update-Auto Dispo

        /// <summary>
        /// Returns review date statistics for the selected organization (from header)
        /// </summary>
        /// <returns>The review date statistics for the selected organization </returns>
        /// <response code="200">The review date statistics for the selected organization  </response>
        // GET: api/cases/reviewDateStatistic
        [AccessType(AccessType.OrgAdmin)]
        [HttpGet, Route("reviewDateStatistic")]
        public async Task<IActionResult> GetReviewDateStatistic()
        {
            var stats = await caseHelper.GetReviewDateStatistic(SelectedOrganizationID);
            return Ok(stats);
        }

        /// <summary>
        /// Returns the last CaseCloseOrCount Auto Dispo Job and the last mass update review date auto dispo job
        /// Thsi is called on a timer
        /// </summary>
        /// <returns>The auto disposition jobs</returns>
        /// <response code="200">Returns 2 Auto Dispo Jobs</response>
        // GET: api/cases/emptyOrWithDisposedItemsCount
        [AccessType(AccessType.OrgAdmin)]
        [HttpGet, Route("emptyOrWithDisposedItemsCount")]
        public async Task<IActionResult> EmptyOrWithDisposedItemsCount()
        {
            var autoDispoJobs = await autoDispoHelper.GetLastAutoDispoJobs(SelectedOrganizationID);

            return Ok(new
            {
                countOrCloseCasesJob = autoDispoJobs.Item1,
                lastMassUpdateReviewDatesJob = autoDispoJobs.Item2
            });
        }

        /// <summary>
        /// Request to count cases that can be closed
        /// </summary>
        /// <returns></returns>
        /// <response code="200"></response>
        // GET: api/cases/requestCountingCasesToClose
        [AccessType(AccessType.OrgAdmin)]
        [HttpGet, Route("requestCountingCasesToClose")]
        public async Task<IActionResult> RequestCountingCasesToClose()
        {
            await autoDispoHelper.AutoDispoJobRequestCounting(SelectedOrganizationID);
            return Ok();
        }

        /// <summary>
        /// Returns paginated list of cases to be closed for the given auto dispo job id
        /// </summary>
        /// <param name="page">The pagination parameters for the request</param>
        /// <returns>A paginated list of cases to be closed by the given job</returns>
        /// <response code="200">A paginated list of cases to be closed by the given job</response>
        /// <response code="400">The pagination parameters are invalid (less than or equal to 0)</response>
        /// <response code="404">The auto disposition job not found</response>
        // GET: api/cases/autoDispoCasesToBeClosed
        [AccessType(AccessType.OrgAdmin)]
        [HttpPost, Route("autoDispoCasesToBeClosed")]
        public async Task<IActionResult> GetCasesToBeClosed(CasesToBeClosedViewModel page)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var autoDispoJob = await autoDispoHelper.GetAutoDispoJobById(page.JobId, SelectedOrganizationID);
            if (autoDispoJob == null)
            {
                return NotFound($"Auto Dispo Job with id: {autoDispoJob} not found.".ToError());
            }

            var casesToBeClosed = await autoDispoHelper.GetCasesToCloseFromJob(autoDispoJob, page.PageNumber);

            return Ok(casesToBeClosed);
        }

        /// <summary>
        /// Returns a paginated list of cases to be reviewed 
        /// </summary>
        /// <param name="casesPageNumber">The pagination parameters for the request</param>
        /// <returns>A paginated list of cases to be closed by the given job</returns>
        /// <response code="200">A paginated list of cases to be reviewed</response>
        /// <response code="400">The pagination parameters are invalid (less than or equal to 0)</response>
        // GET: api/cases/autoDispoCasesToBeReviewed/1
        [AccessType(AccessType.OrgAdmin)]
        [HttpGet, Route("autoDispoCasesToBeReviewed/{casesPageNumber}")]
        public async Task<IActionResult> GetCasesToBeReviewed(int casesPageNumber)
        {
            if (casesPageNumber < 0)
            {
                return BadRequest("Paging parameters are out of range.".ToError());
            }

            var casesToBeReviewed = await caseAutoDispoHelper.AutoDispoCasesToBeReviewed(SelectedOrganizationID, casesPageNumber);

            return Ok(casesToBeReviewed);
        }

        /// <summary>
        /// Returns the count of cases with past review date and without tasks
        /// </summary>
        /// <returns>The count of cases with past review date and without tasks</returns>
        /// <response code="200">The count of cases with past review date and without tasks</response>        
        [AccessType(AccessType.OrgAdmin)]
        [HttpGet, Route("withPastReviewDateAndWithoutTasksCount")]
        public async Task<IActionResult> WithPastReviewDateAndWithoutTasksCount()
        {
            const int limit = 5000;

            var casesCount = await caseAutoDispoHelper.GetWithPastReviewDateAndWithoutOpenTasksCount(SelectedOrganizationID);

            string result;
            result = casesCount > limit
                ? limit + "+"
                : casesCount.ToString();

            return Ok(new
            {
                CasesCount = result
            });
        }

        /// <summary>
        /// Makes a request to perform mass case close
        /// </summary>
        /// <param name="closeCasesSettings">The parameters to perform mass case close (job id, closed date, active)</param>
        /// <returns></returns>
        /// <response code="200"></response>
        /// <response code="400">The job id is invalid (less than or equal to 0)</response>
        /// <response code="404">The auto disposition job was not found</response>
        [AccessType(AccessType.OrgAdmin)]
        [HttpPost, Route("massClose")]
        public async Task<IActionResult> MassCloseCases(CloseCasesSettings closeCasesSettings)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }
            var result = await autoDispoHelper.RequestMassCaseClose(closeCasesSettings, UserId, SelectedOrganizationID);
            if (!result)
            {
                return NotFound($"AutoDispo Job with id - {closeCasesSettings.JobId} not found.");
            }

            return Ok();
        }

        /// <summary>
        /// Makes a request to perform mass update of cases review dates
        /// </summary>
        /// <param name="updateSettings">The parameters to perform mass update of case review dates</param>
        /// <returns></returns>
        /// <response code="200"></response>
        /// <response code="400">The parameters are invalid (from date is greater than to date)</response>
        // GET: api/cases/massUpdateReviewDates
        [AccessType(AccessType.OrgAdmin)]
        [HttpPost, Route("massUpdateReviewDates")]
        public async Task<IActionResult> MassUpdateReviewDates(MassUpdateReviewDatesSettings updateSettings)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            await autoDispoHelper.AutoDispoJobMassUpdateReviewDates(updateSettings, UserId, SelectedOrganizationID);

            return Ok();
        }

        #endregion

        #region Case Auto Dispo

        /// <summary>
        /// Check if the case has undisposed items
        /// </summary>
        /// <param name="id">The id of the case to check on</param>
        /// <returns>A boolean result if the case has undisposed items</returns>
        /// <response code="200">A boolean result if the case has undisposed items</response>
        /// <response code="404">The case id was not found</response>
        [AccessType(AccessType.Any, true)]
        [HttpGet, Route("{id}/hasNotDisposedItems")]
        public async Task<IActionResult> GetHasNotDisposedItemsStatus(long id)
        {
            var (foundCase, result) = await caseHelper.HasUndisposedItems(id, SelectedOrganizationID);

            if (!foundCase)
            {
                return NotFound("Case not found".ToError());
            }
            return Ok(new { status = result });
        }

        /// <summary>
        /// Check if the case is closed
        /// </summary>
        /// <param name="id">The id of the case to check on</param>
        /// <returns>A boolean result if the case is closed</returns>
        /// <response code="200">A boolean result if the case is closed</response>
        /// <response code="404">The case id was not found</response>
        [AccessType(AccessType.Any, true)]
        [HttpGet, Route("{id}/isClosed")]
        public async Task<IActionResult> IsCaseClosed(long id)
        {
            var (foundCase, result) = await caseHelper.IsCaseClosed(id, SelectedOrganizationID);
            if (!foundCase)
            {
                return NotFound("Case not found".ToError());
            }

            return Ok(new { status = result });
        }
        #endregion

        #region Mass Update for Cases

        /// <summary>
        /// Mass update cases
        /// </summary>
        /// <param name="casesToUpdate">The cases to update</param>
        /// <returns></returns>
        /// <response code="200"></response>
        /// <response code="400">The cases contain invalid data (notes length could be too long)</response>
        /// <response code="409">The one of the cases case number is blank or duplicate case number was found</response>
        // PUT: api/cases/MassUpdateCases
        [AccessType(AccessType.Update, true)]
        [HttpPut, Route("MassUpdateCases")]
        public async Task<IActionResult> MassUpdateCases(CaseViewModel[] casesToUpdate)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var hasAdminAccess = await HasAdminAccess();

            // Case-Level Permissions
            if (!hasAdminAccess)
            {
                caseLevelPermissionsHelper.PopulateBase(SelectedOrganizationID);
                var isAnyCaseUnavailableForUpdate = await caseLevelPermissionsHelper
                    .IsAnyCaseUnavailable(casesToUpdate, AccessType.Update, UserId);
                if (isAnyCaseUnavailableForUpdate)
                {
                    var errorMessage = translationsHelperBase.GetTranslationOrEmpty("ITEM.NO_UPDATE_PERMISSIONS",
                        await CurrentLanguageTemplateId());
                    return BadRequest(errorMessage.ToError());
                }
            }

            // Read request headers
            TagsAction tagsAction = requestHelper.GetHeaderValue(Request, "TagsAction", TagsAction.None, Enum.TryParse);
            bool validateClosedDate = requestHelper.GetHeaderValue(Request, "ValidateClosedDate", false, bool.TryParse);
            bool validateReviewDate = requestHelper.GetHeaderValue(Request, "ValidateReviewDate", false, bool.TryParse);

            var result = await caseMassUpdate.Update(casesToUpdate, validateClosedDate, validateReviewDate, tagsAction, UserId, SelectedOrganizationID,
                SelectedOfficeID, await CurrentLanguageTemplateId(), hasAdminAccess);
            return result;
        }

        #endregion

        #region Case Level Permissions

        /// <summary>
        /// Returns case level permissions for the given case id
        /// </summary>
        /// <param name="caseId">The case id</param>
        /// <returns>Case level permissions for the case</returns>
        /// <response code="200">Case level permissions for the case</response>
        /// <response code="404">Case not found</response>
        // GET: api/cases/5/isCaseRestrictedByCLP
        [AccessType(AccessType.AnyInOrg)]
        [HttpGet, Route("{caseId}/isCaseRestrictedByCLP")]
        public async Task<IActionResult> IsCaseRestrictedByCLP(long caseId)
        {
            caseLevelPermissionsHelper.PopulateBase(SelectedOrganizationID);
            var isCaseRestrictedByCLP = await caseLevelPermissionsHelper
                    .AnyEntityEntriesRestrictedByCLP(new CLPEntityIdAndType(caseId, PermissionsEntityType.Case));

            return Ok(new { IsCaseRestrictedByCLP = isCaseRestrictedByCLP });
        }

        // GET: api/cases/5/permissions
        [AccessType(AccessType.OrgAdmin)]
        [HttpGet, Route("{caseId}/permissions")]
        public async Task<IActionResult> GetPermissions(long caseId)
        {
            var caseBson = await mongoCaseHandler.GetCaseBsonById(caseId, SelectedOrganizationID, true);
            if (caseBson == null)
            {
                return NotFound();
            }

            // Get Case Level Permissions matrix for Case
            caseLevelPermissionsHelper.PopulateBase(SelectedOrganizationID);
            var casePermissions = await caseLevelPermissionsHelper.GetCaseLevelPermissions(caseId);

            return Ok(casePermissions);
        }

        /// <summary>
        /// Set case level permissions for the given case id
        /// </summary>
        /// <param name="caseId">The case id</param>
        /// <param name="casePermissions">The case level permissions to set on the case</param>
        /// <returns></returns>
        /// <response code="200"></response>
        /// <response code="400">The case permissions are invalid (null)</response>
        /// <response code="404">Case not found</response>
        // POST: api/cases/5/permissions
        [AccessType(AccessType.OrgAdmin)]
        [HttpPost, Route("{caseId}/permissions")]
        public async Task<IActionResult> SetPermissions(long caseId, CaseLevelPermissions casePermissions)
        {
            // Set Case Level Permissions for Case
            if (casePermissions == null)
            {
                return BadRequest();
            }

            var caseBson = await mongoCaseHandler.GetCaseBsonById(caseId, SelectedOrganizationID, true);
            if (caseBson == null)
            {
                return NotFound();
            }

            caseLevelPermissionsHelper.PopulateBase(SelectedOrganizationID);
            await caseLevelPermissionsHelper.SetCaseLevelPermissions(caseId, casePermissions, await CurrentLanguageTemplateId());

            return Ok();
        }

        /// <summary>
        /// Returns if the case can be restricted by case level permissions
        /// </summary>
        /// <param name="caseId">The case id</param>
        /// <returns></returns>
        /// <response code="200"></response>
        /// <response code="400">The case can not be restricted</response>
        /// <response code="404">The case was not found</response>
        // GET: api/cases/5/canRestrict
        [AccessType(AccessType.OrgAdmin)]
        [HttpGet, Route("{caseId}/canRestrict")]
        public async Task<IActionResult> CanRestrict(long caseId)
        {
            var caseBson = await mongoCaseHandler.GetCaseBsonById(caseId, SelectedOrganizationID, true);
            if (caseBson == null)
            {
                return NotFound();
            }

            try
            {
                caseLevelPermissionsHelper.PopulateBase(SelectedOrganizationID);
                caseLevelPermissionsHelper.CheckIfTheCaseCanBeRestricted(caseId, await CurrentLanguageTemplateId());
            }
            catch (CaseCannotBeRestrictedException ex)
            {
                return BadRequest(ex.Message.ToError());
            }
            return Ok();
        }

        #endregion

        #region Recent Cases

        /// <summary>
        /// Returns recent cases for the current user
        /// </summary>
        /// <returns>List of 10 (or less) recent cases</returns>
        /// <response code="200">List of 10 (or less) recent cases</response>
        [AccessType(AccessType.Any)]
        [HttpGet, Route("recent")]
        public async Task<IActionResult> GetRecentCases()
        {
            var allUserOffices = await permissionsHelper.AllUserOfficeIds(await HasAdminAccess(), SelectedOrganizationID, UserId);
            var recentCases = await recentCasesHelper.GetRecentCases(SelectedOrganizationID, allUserOffices, UserId);
            return Ok(recentCases);
        }

        /// <summary>
        /// Returns most recent case for the current user
        /// </summary>
        /// <returns>Most recent case</returns>
        /// <response code="200">Most recent case</response>
        [AccessType(AccessType.AnyInOrg)]
        [HttpGet, Route("mostRecent")]
        public async Task<IActionResult> GetMostRecentCase()
        {
            var allUserOffices = await permissionsHelper.AllUserOfficeIds(await HasAdminAccess(), SelectedOrganizationID, UserId);
            var mostRecentCase = await recentCasesHelper.GetMostRecentCase(SelectedOrganizationID, allUserOffices, UserId);
            return Ok(mostRecentCase);
        }

        #endregion

        /// <summary>
        /// Update tags to a case
        /// </summary>
        /// <returns></returns>
        /// <param name="id">The case id</param>
        /// <param name="tags">The tags to use to update case</param>
        /// <response code="200"></response>
        /// <response code="400">The tags list is invalid (null)</response>
        /// <response code="404">The case was not found</response>
        // POST: api/cases/5/saveTags
        [HttpPost, Route("{id}/saveTags")]
        [AccessType(AccessType.Update, true)]
        public async Task<IActionResult> SaveTags(long id, [FromBody] List<TagViewModel> tags)
        {
            if (id <= 0)
            {
                return NotFound();
            }

            if (tags == null)
            {
                return BadRequest();
            }

            var (caseFound, error) = await tagUpdateHelper.SaveTags(id, UserId, tags, await HasAdminAccess(), SelectedOrganizationID, SelectedOfficeID,
                await CurrentLanguageTemplateId());
            if (!caseFound)
            {
                return NotFound();
            }
            if (!string.IsNullOrEmpty(error))
            {
                return BadRequest(error.ToError());
            }
            return Ok();
        }

        /// <summary>
        /// Get transaction media count for item / case
        /// </summary>
        /// <param name="id">the case id</param>
        /// <returns>item media amount / transaction media amount</returns>
        // GET: api/cases/5/itemsMediaAmount
        [AccessType(AccessType.List, true)]
        [HttpGet, Route("{id:int}/transactionsMediaAmount")]
        public async Task<IActionResult> GetTransactionsMediaAmount(long id)
        {
            var media = await caseMediaCtrlHelper.GetCaseTransactionsMedia(id, SelectedOrganizationID,
                await RestrictMediaToOwner(), UserId);
            var itemsMediaAmount = media == null ? 0 : media.Count();
            var countLimitExceeded = itemsMediaAmount > MaxTranactionalMediaCount;

            return Ok(new { Count = itemsMediaAmount, CountLimitExceeded = countLimitExceeded });
        }

        /// <summary>
        /// Return transaction media page for case items
        /// </summary>
        /// <param name="id">The case id</param>
        /// <param name="searchParameters">The search parameter object</param>
        /// <response code="400">Invalid search parameters</response>
        /// <returns>List of media</returns>
        // POST: api/cases/5/transactionsMedia
        [AccessType(AccessType.List, true)]
        [HttpPost, Route("{id:int}/transactionsMedia")]
        public async Task<IActionResult> GetTransactionsMedia(long id, [FromBody] SearchViewModelBase searchParameters)
        {
            // Validation
            var errorMessage = searchModelValidation.Validate(searchParameters);
            if (!string.IsNullOrWhiteSpace(errorMessage))
            {
                return BadRequest(errorMessage.ToError());
            }

            var mediaQuery = await caseMediaCtrlHelper.GetCaseTransactionsMedia(id, SelectedOrganizationID,
                await RestrictMediaToOwner(), UserId);
            if (mediaQuery == null)
            {
                return Ok(new { Media = new List<Medium>(), MediaCountWithChildren = 0 });
            }

            var mediaCount = mediaQuery.Count();
            if (searchParameters != null)
            {
                mediaQuery = searchParameters.OrderByAsc ?
                    mediaQuery.OrderBy(searchParameters.OrderBy) :
                    mediaQuery.OrderBy($"{searchParameters.OrderBy} descending");

                mediaQuery = mediaQuery.Skip(searchParameters.PageSize * (searchParameters.PageNumber - 1))
                                       .Take(searchParameters.PageSize);
            }

            var media = await mediaQuery.ToListAsync();
            if (!await HasAdminAccess())
            {
                caseLevelPermissionsHelper.PopulateBase(SelectedOrganizationID);
                await caseLevelPermissionsHelper.StripInaccessibleEntities(media, false, UserId, id);
            }

            var hasThumbnailAccess = await HasThumbnailAccess();
            var restrictThumbnailToOwner = await RestrictThumbnailToOwner();
            var language = await CurrentLanguageTemplateId();

            media.ForEach(x => mediaHelper.SetMediumDetails(x, hasThumbnailAccess, restrictThumbnailToOwner, UserId, language));

            return Ok(new { Entities = media, TotalCount = mediaCount });
        }

        #region Evidence.com

        /// <summary>
        /// Returns if evicence.com is configured for selected organization
        /// </summary>
        /// <returns>bool if evicence.com is setup</returns>
        [HttpGet, Route("useEvidenceCom")]
        [AccessType(AccessType.List)]
        public async Task<IActionResult> IsEvidenceComConfigured()
        {
            var org = await db.Organizations.SingleAsync(x => x.Id == SelectedOrganizationID);
            return Ok(org.EcEnabled);
        }

        /// <summary>
        /// Get Evidence.com integration data for a case
        /// </summary>
        /// <param name="search">The model to perform the search</param>
        /// <response code="400">Invalid search parameters or invalid organization configuration for evidence.com</response>
        /// <returns>Evidence.com data</returns>
        [HttpPost, Route("evidenceCom")]
        [AccessType(AccessType.List)]
        [BasicCache(5)]
        public async Task<IActionResult> GetEvidenceCom([FromBody] EcSearchViewModel search)
        {
            if (search == null || search.CaseId <= 0 || search.PageNumber < 0 || search.PageSize > 100 || search.PageSize <= 0)
            {
                return BadRequest();
            }

            var org = await db.Organizations.SingleAsync(x => x.Id == SelectedOrganizationID);
            var worker = ecWorkflowFactory.GetWorkflow(org.EcUseAlternateWorkflow);
            var results = await worker.Execute(SelectedOrganizationID, search);

            return Ok(results);
        }

        /// <summary>
        /// Test Evidence.com connection
        /// </summary>
        /// <param name="request">The parameters for evidence.com usage</param>
        /// <returns>Boolean if login is successful</returns>
        [HttpPost, Route("evidenceComLoginTest")]
        [AccessType(AccessType.OrgAdmin)]
        public async Task<IActionResult> TestEvicenceComLogin([FromBody] EcTestLoginRequest request)
        {
            var result = await ecGenUtils.TestLogin(request);
            return Ok(result);
        }

        /// <summary>
        /// Get evidence count for a case (from Evidence.com)
        /// </summary>
        /// <param name="caseId">The case Id</param>
        /// <response code="400">Invalid case id</response>
        /// <returns>Evidence count for a case</returns>
        [HttpGet, Route("evidenceComCount/{caseId}")]
        [AccessType(AccessType.List)]
        [BasicCache(5)]
        public async Task<IActionResult> GetEvidenceComCountForCase(long caseId)
        {
            if (caseId <= 0)
            {
                return BadRequest();
            }

            var org = await db.Organizations.SingleAsync(x => x.Id == SelectedOrganizationID);
            var worker = ecWorkflowFactory.GetWorkflow(org.EcUseAlternateWorkflow);
            var result = await worker.GetCountForCase(SelectedOrganizationID, caseId);

            return Ok(result);
        }

        /// <summary>
        /// Returns direct link to Evidence.com
        /// </summary>
        /// <param name="evidenceId">The evidence id to get link for</param>
        /// <response code="400">Invalid evidence id or failed to retrieve link from Evidence.com</response>
        /// <returns>The link to Evidence.com</returns>
        [HttpGet, Route("evidenceComLink/{evidenceId}")]
        [AccessType(AccessType.List)]
        [BasicCache(5)]
        public async Task<IActionResult> GetEvidenceComDirectLink(string evidenceId)
        {
            if (string.IsNullOrWhiteSpace(evidenceId))
            {
                return BadRequest();
            }
            var link = await ecGenUtils.GetEvidenceLink(SelectedOrganizationID, evidenceId);
            if (string.IsNullOrWhiteSpace(link))
            {
                return BadRequest();
            }
            return Ok(link);
        }
        #endregion


    }

}
