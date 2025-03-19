using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace LetsCarpool.Server.Controllers
{
    public class NotificationsController : Controller
    {
        // GET: NotificationsController
        public ActionResult Index()
        {
            return View();
        }

        // GET: NotificationsController/Details/5
        public ActionResult Details(int id)
        {
            return View();
        }

        // GET: NotificationsController/Create
        public ActionResult Create()
        {
            return View();
        }

        // POST: NotificationsController/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Create(IFormCollection collection)
        {
            try
            {
                return RedirectToAction(nameof(Index));
            }
            catch
            {
                return View();
            }
        }

        // GET: NotificationsController/Edit/5
        public ActionResult Edit(int id)
        {
            return View();
        }

        // POST: NotificationsController/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Edit(int id, IFormCollection collection)
        {
            try
            {
                return RedirectToAction(nameof(Index));
            }
            catch
            {
                return View();
            }
        }

        // GET: NotificationsController/Delete/5
        public ActionResult Delete(int id)
        {
            return View();
        }

        // POST: NotificationsController/Delete/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Delete(int id, IFormCollection collection)
        {
            try
            {
                return RedirectToAction(nameof(Index));
            }
            catch
            {
                return View();
            }
        }
    }
}
