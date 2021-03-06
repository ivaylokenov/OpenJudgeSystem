﻿namespace OJS.Web.Areas.Administration.Controllers
{
    using System.Collections;
    using System.Linq;
    using System.Web.Mvc;

    using OJS.Common.Extensions;
    using OJS.Common.Models;
    using OJS.Data;
    using OJS.Web.Areas.Administration.Controllers.Common;
    using OJS.Web.Areas.Administration.ViewModels.Lecturers;

    public class LecturersController : AdministrationBaseGridController
    {
        public LecturersController(IOjsData data)
            : base(data)
        {
        }

        public override IEnumerable GetData()
        {
            var lecturerRoleName = SystemRole.Lecturer.GetDescription();

            var lectureRoleId = this.Data.Roles
                .All()
                .Where(x => x.Name == lecturerRoleName)
                .Select(x => x.Id)
                .FirstOrDefault();

            return this.Data.Users
                .All()
                .Where(x => x.Roles.Any(y => y.RoleId == lectureRoleId))
                .Select(LecturerGridViewModel.ViewModel);
        }

        public override object GetById(object id)
        {
            return this.Data.Users.GetById((string)id);
        }

        public override string GetEntityKeyName()
        {
            return this.GetEntityKeyNameByType(typeof(LecturerGridViewModel));
        }

        public ActionResult Index()
        {
            return this.View();
        }
    }
}