using System.Threading.Tasks;
using PickNBook.Api.Models.DTOs;

namespace PickNBook.Api.Services;

public interface IAboutUsService
{
    Task<AboutUsDto?> GetAsync(string module);
    Task UpdateAsync(UpdateAboutUsDto dto);
}
