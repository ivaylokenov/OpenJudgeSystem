﻿namespace OJS.Web.Areas.Administration.Controllers
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Web;
    using System.Web.Mvc;

    using Kendo.Mvc.Extensions;
    using Kendo.Mvc.UI;

    using MissingFeatures;

    using OJS.Common;
    using OJS.Common.Extensions;
    using OJS.Common.Models;
    using OJS.Data;
    using OJS.Data.Models;
    using OJS.Web.Areas.Administration.Controllers.Common;
    using OJS.Web.Areas.Administration.ViewModels.Contest;
    using OJS.Web.Areas.Administration.ViewModels.Problem;
    using OJS.Web.Areas.Administration.ViewModels.ProblemResource;
    using OJS.Web.Areas.Administration.ViewModels.Submission;
    using OJS.Web.Common;
    using OJS.Web.Common.Extensions;
    using OJS.Web.Common.ZippedTestManipulator;
    using OJS.Web.ViewModels.Common;

    using GlobalResource = Resources.Areas.Administration.Problems.ProblemsControllers;
    using TransactionScope = System.Transactions.TransactionScope;

    public class ProblemsController : LecturerBaseController
    {
        public ProblemsController(IOjsData data)
            : base(data)
        {
        }

        public ActionResult Index()
        {
            return this.View();
        }

        public ActionResult Contest(int? id)
        {
            if (id == null || !this.CheckIfUserHasContestPermissions(id.Value))
            {
                this.TempData[GlobalConstants.DangerMessage] = "Нямате привилегиите за това действие";
                return this.RedirectToAction("Index", "Contests", new { area = "Administration" });
            }

            this.ViewBag.ContestId = id;

            return this.View(GlobalConstants.Index);
        }

        public ActionResult Resource(int? id)
        {
            if (id == null || !this.CheckIfUserHasProblemPermissions(id.Value))
            {
                this.TempData[GlobalConstants.DangerMessage] = "Нямате привилегиите за това действие";
                return this.RedirectToAction("Index", "Contests", new { area = "Administration" });
            }

            var problem = this.Data.Problems
                .All()
                .FirstOrDefault(pr => pr.Id == id);

            if (problem == null)
            {
                this.TempData.AddDangerMessage(GlobalResource.Invalid_problem);
                return this.RedirectToAction(GlobalConstants.Index);
            }

            this.ViewBag.ContestId = problem.ContestId;
            this.ViewBag.ProblemId = problem.Id;

            return this.View(GlobalConstants.Index);
        }

        [HttpGet]
        public ActionResult Create(int? id)
        {
            if (id == null)
            {
                this.TempData.AddDangerMessage(GlobalResource.Invalid_contest);
                return this.RedirectToAction(GlobalConstants.Index);
            }

            if (!this.CheckIfUserHasContestPermissions(id.Value))
            {
                this.TempData[GlobalConstants.DangerMessage] = "Нямате привилегиите за това действие";
                return this.RedirectToAction("Index", "Contests", new { area = "Administration" });
            }

            var contest = this.Data.Contests.All().FirstOrDefault(x => x.Id == id);

            if (contest == null)
            {
                this.TempData.AddDangerMessage(GlobalResource.Invalid_contest);
                return this.RedirectToAction(GlobalConstants.Index);
            }

            var checkers = this.Data.Checkers.All()
                .Select(x => x.Name);

            var lastOrderBy = -1;
            var lastProblem = this.Data.Problems.All().Where(x => x.ContestId == id);

            if (lastProblem.Any())
            {
                lastOrderBy = lastProblem.Max(x => x.OrderBy);
            }

            var problem = new DetailedProblemViewModel
            {
                Name = "Име",
                MaximumPoints = 100,
                TimeLimit = 100,
                MemoryLimit = 16777216,
                AvailableCheckers = this.Data.Checkers.All().Select(checker => new SelectListItem { Text = checker.Name, Value = checker.Name, Selected = checker.Name.Contains("Trim") }),
                OrderBy = lastOrderBy + 1,
                ContestId = contest.Id,
                ContestName = contest.Name,
                ShowResults = true,
                SourceCodeSizeLimit = 16384,
                ShowDetailedFeedback = false,
            };

            return this.View(problem);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Create(int id, HttpPostedFileBase testArchive, DetailedProblemViewModel problem)
        {
            if (!this.CheckIfUserHasContestPermissions(id))
            {
                this.TempData[GlobalConstants.DangerMessage] = "Нямате привилегиите за това действие";
                return this.RedirectToAction("Index", "Contests", new { area = "Administration" });
            }

            if (problem.Resources != null && problem.Resources.Any())
            {
                var validResources = problem.Resources
                    .All(res => !string.IsNullOrEmpty(res.Name) &&
                        ((res.Type == ProblemResourceType.AuthorsSolution && res.File != null && res.File.ContentLength > 0) ||
                        (res.Type == ProblemResourceType.ProblemDescription && res.File != null && res.File.ContentLength > 0) ||
                        (res.Type == ProblemResourceType.Link && !string.IsNullOrEmpty(res.Link))));

                if (!validResources)
                {
                    this.ModelState.AddModelError("Resources", GlobalResource.Resources_not_complete);
                }
            }

            if (this.ModelState.IsValid)
            {
                var newProblem = new Problem
                {
                    Name = problem.Name,
                    ContestId = id,
                    MaximumPoints = problem.MaximumPoints,
                    MemoryLimit = problem.MemoryLimit,
                    TimeLimit = problem.TimeLimit,
                    SourceCodeSizeLimit = problem.SourceCodeSizeLimit,
                    ShowResults = problem.ShowResults,
                    ShowDetailedFeedback = problem.ShowDetailedFeedback,
                    OrderBy = problem.OrderBy,
                    Checker = this.Data.Checkers.All().FirstOrDefault(x => x.Name == problem.Checker),
                    SolutionSkeleton = problem.SolutionSkeletonData
                };

                if (problem.Resources != null && problem.Resources.Any())
                {
                    this.AddResourcesToProblem(newProblem, problem.Resources);
                }

                if (testArchive != null && testArchive.ContentLength != 0)
                {
                    try
                    {
                        this.AddTestsToProblem(newProblem, testArchive);
                    }
                    catch (Exception ex)
                    {
                        // TempData is not working with return this.View
                        var systemMessages = new SystemMessageCollection
                                {
                                    new SystemMessage
                                    {
                                        Content = ex.Message,
                                        Type = SystemMessageType.Error,
                                        Importance = 0
                                    }
                                };
                        this.ViewBag.SystemMessages = systemMessages;
                        problem.AvailableCheckers = this.Data.Checkers.All().Select(checker => new SelectListItem { Text = checker.Name, Value = checker.Name });
                        return this.View(problem);
                    }
                }

                this.Data.Problems.Add(newProblem);
                this.Data.SaveChanges();

                this.TempData.AddInfoMessage(GlobalResource.Problem_added);
                return this.RedirectToAction("Problem", "Tests", new { newProblem.Id });
            }

            problem.AvailableCheckers = this.Data.Checkers.All().Select(checker => new SelectListItem { Text = checker.Name, Value = checker.Name });

            return this.View(problem);
        }

        [HttpGet]
        public ActionResult Edit(int? id)
        {
            if (id == null)
            {
                this.TempData.AddDangerMessage(GlobalResource.Invalid_problem);
                return this.RedirectToAction(GlobalConstants.Index);
            }

            if (!this.CheckIfUserHasProblemPermissions(id.Value))
            {
                this.TempData[GlobalConstants.DangerMessage] = "Нямате привилегиите за това действие";
                return this.RedirectToAction("Index", "Contests", new { area = "Administration" });
            }

            var selectedProblem = this.Data.Problems.All()
                .Where(x => x.Id == id)
                .Select(DetailedProblemViewModel.FromProblem)
                .FirstOrDefault();

            if (selectedProblem == null)
            {
                this.TempData.AddDangerMessage(GlobalResource.Invalid_problem);
                return this.RedirectToAction(GlobalConstants.Index);
            }

            var checkers = this.Data.Checkers
                .All()
                .AsQueryable()
                .Select(checker => new SelectListItem { Text = checker.Name, Value = checker.Name });

            selectedProblem.AvailableCheckers = checkers;

            return this.View(selectedProblem);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Edit(int id, DetailedProblemViewModel problem)
        {
            if (!this.CheckIfUserHasProblemPermissions(id))
            {
                this.TempData[GlobalConstants.DangerMessage] = "Нямате привилегиите за това действие";
                return this.RedirectToAction("Index", "Contests", new { area = "Administration" });
            }

            if (problem != null && this.ModelState.IsValid)
            {
                var existingProblem = this.Data.Problems.All().FirstOrDefault(x => x.Id == id);

                existingProblem.Name = problem.Name;
                existingProblem.MaximumPoints = problem.MaximumPoints;
                existingProblem.TimeLimit = problem.TimeLimit;
                existingProblem.MemoryLimit = problem.MemoryLimit;
                existingProblem.SourceCodeSizeLimit = problem.SourceCodeSizeLimit;
                existingProblem.ShowResults = problem.ShowResults;
                existingProblem.ShowDetailedFeedback = problem.ShowDetailedFeedback;
                existingProblem.Checker = this.Data.Checkers.All().FirstOrDefault(x => x.Name == problem.Checker);
                existingProblem.OrderBy = problem.OrderBy;
                existingProblem.SolutionSkeleton = problem.SolutionSkeletonData;
                existingProblem.ContestId = problem.ContestId;

                this.Data.SaveChanges();

                this.TempData.AddInfoMessage(GlobalResource.Problem_edited);
                return this.RedirectToAction("Contest", new { id = existingProblem.ContestId });
            }

            problem.AvailableCheckers = this.Data.Checkers.All().Select(checker => new SelectListItem { Text = checker.Name, Value = checker.Name });
            return this.View(problem);
        }

        [HttpGet]
        public ActionResult Delete(int? id)
        {
            if (id == null)
            {
                this.TempData.AddDangerMessage(GlobalResource.Invalid_problem);
                return this.RedirectToAction(GlobalConstants.Index);
            }

            if (!this.CheckIfUserHasProblemPermissions(id.Value))
            {
                this.TempData[GlobalConstants.DangerMessage] = "Нямате привилегиите за това действие";
                return this.RedirectToAction("Index", "Contests", new { area = "Administration" });
            }

            var selectedProblem = this.Data.Problems.All()
                .Where(x => x.Id == id)
                .Select(DetailedProblemViewModel.FromProblem)
                .FirstOrDefault();

            if (selectedProblem == null)
            {
                this.TempData.AddDangerMessage(GlobalResource.Invalid_problem);
                return this.RedirectToAction(GlobalConstants.Index);
            }

            return this.View(selectedProblem);
        }

        public ActionResult ConfirmDelete(int? id)
        {
            if (id == null)
            {
                this.TempData.AddDangerMessage(GlobalResource.Invalid_problem);
                return this.RedirectToAction(GlobalConstants.Index);
            }

            var problem = this.Data.Problems.All().FirstOrDefault(x => x.Id == id);

            if (problem == null)
            {
                this.TempData.AddDangerMessage(GlobalResource.Invalid_problem);
                return this.RedirectToAction(GlobalConstants.Index);
            }

            this.Data.Resources.Delete(r => r.ProblemId == id);

            this.Data.TestRuns.Delete(tr => tr.Submission.ProblemId == id);

            this.Data.Tests.Delete(t => t.ProblemId == id);

            this.Data.Submissions.Delete(s => s.ProblemId == id);

            this.Data.Problems.Delete(id.Value);

            this.Data.SaveChanges();

            this.TempData.AddInfoMessage(GlobalResource.Problem_deleted);
            return this.RedirectToAction("Contest", new { id = problem.ContestId });
        }

        [HttpGet]
        public ActionResult DeleteAll(int? id)
        {
            if (id == null)
            {
                this.TempData.AddDangerMessage(GlobalResource.Invalid_contest);
                return this.RedirectToAction(GlobalConstants.Index);
            }

            if (!this.CheckIfUserHasContestPermissions(id.Value))
            {
                this.TempData[GlobalConstants.DangerMessage] = "Нямате привилегиите за това действие";
                return this.RedirectToAction("Index", "Contests", new { area = "Administration" });
            }

            var contest = this.Data.Contests.All()
                .Where(x => x.Id == id)
                .Select(ContestAdministrationViewModel.ViewModel)
                .FirstOrDefault();

            if (contest == null)
            {
                this.TempData.AddDangerMessage(GlobalResource.Invalid_contest);
                return this.RedirectToAction(GlobalConstants.Index);
            }

            return this.View(contest);
        }

        public ActionResult ConfirmDeleteAll(int? id)
        {
            if (id == null || !this.Data.Contests.All().Any(x => x.Id == id))
            {
                this.TempData.AddDangerMessage(GlobalResource.Invalid_contest);
                return this.RedirectToAction<ProblemsController>(x => x.Index());
            }

            if (!this.CheckIfUserHasContestPermissions(id.Value))
            {
                this.TempData.AddDangerMessage(GlobalResource.Invalid_contest);
                return this.RedirectToAction(GlobalConstants.Index);
            }

            this.Data.Resources.Delete(r => r.Problem.ContestId == id);

            this.Data.TestRuns.Delete(tr => tr.Submission.Problem.ContestId == id);

            this.Data.Tests.Delete(t => t.Problem.ContestId == id);

            this.Data.Submissions.Delete(s => s.Problem.ContestId == id);

            this.Data.Problems.Delete(p => p.ContestId == id);

            this.Data.SaveChanges();

            this.TempData.AddInfoMessage(GlobalResource.Problems_deleted);
            return this.RedirectToAction("Contest", new { id });
        }

        public ActionResult Details(int? id)
        {
            if (id == null)
            {
                this.TempData.AddDangerMessage(GlobalResource.Invalid_problem);
                return this.RedirectToAction(GlobalConstants.Index);
            }

            var problem = this.Data.Problems.All()
                .Where(pr => pr.Id == id)
                .Select(DetailedProblemViewModel.FromProblem)
                .FirstOrDefault();

            if (problem == null)
            {
                this.TempData.AddDangerMessage(GlobalResource.Invalid_problem);
                return this.RedirectToAction(GlobalConstants.Index);
            }

            if (!this.CheckIfUserHasContestPermissions(problem.ContestId))
            {
                this.TempData[GlobalConstants.DangerMessage] = "Нямате привилегиите за това действие";
                return this.RedirectToAction("Index", "Contests", new { area = "Administration" });
            }

            return this.View(problem);
        }

        public ActionResult Retest(int? id)
        {
            if (id == null)
            {
                this.TempData.AddDangerMessage(GlobalResource.Invalid_problem);
                return this.RedirectToAction(GlobalConstants.Index);
            }

            var problem = this.Data.Problems
                .All()
                .FirstOrDefault(pr => pr.Id == id);

            if (problem == null)
            {
                this.TempData.AddDangerMessage(GlobalResource.Invalid_problem);
                return this.RedirectToAction(GlobalConstants.Index);
            }

            if (!this.CheckIfUserHasContestPermissions(problem.ContestId))
            {
                this.TempData[GlobalConstants.DangerMessage] = "Нямате привилегиите за това действие";
                return this.RedirectToAction("Index", "Contests", new { area = "Administration" });
            }

            using (var scope = new TransactionScope())
            {
                this.Data.ParticipantScores.DeleteParticipantScores(id.Value);
                this.Data.SaveChanges();

                this.Data.Submissions.All().Where(s => s.ProblemId == id).Select(s => s.Id).ForEach(this.RetestSubmission);
                this.Data.SaveChanges();

                scope.Complete();
            }

            this.TempData.AddInfoMessage(GlobalResource.Problem_retested);
            return this.RedirectToAction("Contest", new { id = problem.ContestId });
        }

        [HttpGet]
        public ActionResult GetSubmissions(int id)
        {
            if (!this.CheckIfUserHasProblemPermissions(id))
            {
                this.TempData[GlobalConstants.DangerMessage] = "Нямате привилегиите за това действие";
                return this.RedirectToAction("Index", "Contests", new { area = "Administration" });
            }

            this.ViewBag.SubmissionStatusData = DropdownViewModel.GetEnumValues<SubmissionStatus>();
            return this.PartialView("_SubmissionsGrid", id);
        }

        [HttpPost]
        public JsonResult ReadSubmissions([DataSourceRequest]DataSourceRequest request, int id)
        {
            if (!this.CheckIfUserHasProblemPermissions(id))
            {
                return this.Json("No premissions");
            }

            var submissions = this.Data.Submissions
                .All()
                .Where(s => s.ProblemId == id)
                .Select(SubmissionAdministrationGridViewModel.ViewModel);

            return this.Json(submissions.ToDataSourceResult(request));
        }

        [HttpGet]
        public ActionResult GetResources(int id)
        {
            if (!this.CheckIfUserHasProblemPermissions(id))
            {
                this.TempData[GlobalConstants.DangerMessage] = "Нямате привилегиите за това действие";
                return this.RedirectToAction("Index", "Contests", new { area = "Administration" });
            }

            return this.PartialView("_ResourcesGrid", id);
        }

        [HttpPost]
        public ActionResult ReadResources([DataSourceRequest]DataSourceRequest request, int id)
        {
            if (!this.CheckIfUserHasProblemPermissions(id))
            {
                this.TempData[GlobalConstants.DangerMessage] = "Нямате привилегиите за това действие";
                return this.RedirectToAction("Index", "Contests", new { area = "Administration" });
            }

            var resources = this.Data.Resources
                .All()
                .Where(r => r.ProblemId == id)
                .Select(ProblemResourceGridViewModel.FromResource);

            return this.Json(resources.ToDataSourceResult(request));
        }

        [HttpGet]
        public ActionResult ByContest(int id)
        {
            var result = this.GetData(id);
            return this.LargeJson(result);
        }

        [HttpGet]
        public JsonResult GetCascadeCategories()
        {
            var result = this.Data.ContestCategories.All().Select(x => new { x.Id, x.Name });

            return this.Json(result, JsonRequestBehavior.AllowGet);
        }

        [HttpGet]
        public JsonResult GetCascadeContests(string categories)
        {
            var contests = this.Data.Contests.All();

            int categoryId;

            if (int.TryParse(categories, out categoryId))
            {
                contests = contests.Where(x => x.CategoryId == categoryId);
            }

            var result = contests.Select(x => new { x.Id, x.Name });

            return this.Json(result, JsonRequestBehavior.AllowGet);
        }

        [HttpGet]
        public JsonResult GetSearchedContests()
        {
            var result = this.Data.Contests.All().Select(x => new { x.Id, x.Name });
            return this.Json(result, JsonRequestBehavior.AllowGet);
        }

        [HttpGet]
        public JsonResult GetContestInformation(string id)
        {
            // TODO: Add validation for Id
            var contestIdNumber = int.Parse(id);

            if (!this.CheckIfUserHasContestPermissions(contestIdNumber))
            {
                this.TempData[GlobalConstants.DangerMessage] = "Нямате привилегиите за това действие";
                return this.Json("No premissions");
            }

            var contest = this.Data.Contests.All().FirstOrDefault(x => x.Id == contestIdNumber);

            var contestId = contestIdNumber;
            var categoryId = contest.CategoryId;

            var result = new { contest = contestId, category = categoryId };
            return this.Json(result, JsonRequestBehavior.AllowGet);
        }

        [HttpGet]
        public FileResult ExportToExcel([DataSourceRequest] DataSourceRequest request, int contestId)
        {
            if (!this.CheckIfUserHasContestPermissions(contestId))
            {
                this.TempData[GlobalConstants.DangerMessage] = "Нямате привилегиите за това действие";
                throw new UnauthorizedAccessException("No premissions");
            }

            return this.ExportToExcel(request, this.GetData(contestId));
        }

        // TODO: Transfer to ResourcesController
        public ActionResult AddResourceForm(int id)
        {
            // TODO: Add validation for Id
            var resourceViewModel = new ProblemResourceViewModel
            {
                Id = id,
                AllTypes = Enum.GetValues(typeof(ProblemResourceType)).Cast<ProblemResourceType>().Select(v => new SelectListItem
                {
                    Text = v.GetDescription(),
                    Value = ((int)v).ToString(CultureInfo.InvariantCulture)
                })
            };

            return this.PartialView("_ProblemResourceForm", resourceViewModel);
        }

        public ActionResult FullSolutionSkeleton(int id)
        {
            if (!this.CheckIfUserHasProblemPermissions(id))
            {
                return this.Json("Нямате привилегиите за това действие");
            }

            var solutionSkeleton = this.Data.Problems
                .All()
                .Where(p => p.Id == id)
                .Select(p => p.SolutionSkeleton)
                .FirstOrDefault();

            return this.Content(solutionSkeleton.Decompress());
        }

        private IEnumerable GetData(int id)
        {
            if (!this.CheckIfUserHasContestPermissions(id))
            {
                return new List<DetailedProblemViewModel>();
            }

            var result = this.Data.Problems.All()
                .Where(x => x.ContestId == id)
                .OrderBy(x => x.Name)
                .Select(DetailedProblemViewModel.FromProblem);

            return result;
        }

        private void AddResourcesToProblem(Problem problem, IEnumerable<ProblemResourceViewModel> resources)
        {
            var orderCount = 0;

            foreach (var resource in resources)
            {
                if (!string.IsNullOrEmpty(resource.Name) && resource.Type == ProblemResourceType.Link && resource.Link != null)
                {
                    problem.Resources.Add(new ProblemResource
                    {
                        Name = resource.Name,
                        Type = resource.Type,
                        OrderBy = orderCount,
                        Link = resource.Link,
                    });

                    orderCount++;
                }
                else if (!string.IsNullOrEmpty(resource.Name) && resource.Type != ProblemResourceType.Link && resource.File != null)
                {
                    problem.Resources.Add(new ProblemResource
                    {
                        Name = resource.Name,
                        Type = resource.Type,
                        OrderBy = orderCount,
                        File = resource.File.InputStream.ToByteArray(),
                        FileExtension = resource.FileExtension
                    });

                    orderCount++;
                }
            }
        }

        private void AddTestsToProblem(Problem problem, HttpPostedFileBase testArchive)
        {
            var extension = testArchive.FileName.Substring(testArchive.FileName.Length - 4, 4);

            if (extension != GlobalConstants.ZipFileExtension)
            {
                throw new ArgumentException(GlobalResource.Must_be_zip_file);
            }

            using (var memory = new MemoryStream())
            {
                testArchive.InputStream.CopyTo(memory);
                memory.Position = 0;

                var parsedTests = ZippedTestsManipulator.Parse(memory);

                if (parsedTests.ZeroInputs.Count != parsedTests.ZeroOutputs.Count || parsedTests.Inputs.Count != parsedTests.Outputs.Count)
                {
                    throw new ArgumentException(GlobalResource.Invalid_tests);
                }

                ZippedTestsManipulator.AddTestsToProblem(problem, parsedTests);
            }
        }

        private void RetestSubmission(int submissionId)
        {
            var submission = new Submission { Id = submissionId, Processed = false, Processing = false };
            this.Data.Context.Submissions.Attach(submission);
            var entry = this.Data.Context.Entry(submission);
            entry.Property(pr => pr.Processed).IsModified = true;
            entry.Property(pr => pr.Processing).IsModified = true;
        }
    }
}