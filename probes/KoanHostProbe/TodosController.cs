using Koan.Web.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace KoanHostProbe;

[Route("api/todos")]
public sealed class TodosController : EntityController<Todo>;
