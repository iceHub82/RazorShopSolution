﻿using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using RazorShop.Data;
using RazorShop.Data.Entities;
using RazorShop.Web.Models.ViewModels;

namespace RazorShop.Web.Apis;

public static class CartApis
{
    public static void CartApi(this WebApplication app)
    {
        app.MapGet("/cart", async (HttpContext http, RazorShopDbContext db, IMemoryCache cache) =>
        {
            var cart = await GetCart(http, db);
            var items = await GetCartItems(cart.Id, db)!;
            var vm = GetCartViewModel(items!, cache);

            return Results.Extensions.RazorSlice<Slices.Cart, CartVm>(vm!);
        }).NoCache();

        app.MapGet("/cart/add/{id}", async (HttpContext http, RazorShopDbContext db, IMemoryCache cache, int id, int size, int quantity) =>
        {
            var cart = await GetCart(http, db);

            int? sizeId = size == 0 ? null : size;

            var existingCartItem = db.CartItems!.FirstOrDefault(c => c.CartId == cart.Id && c.ProductId == id && c.SizeId == sizeId && !c.Deleted);

            if (existingCartItem == null)
                await db.CartItems!.AddAsync(new CartItem { CartId = cart.Id, ProductId = id, SizeId = sizeId, Quantity = quantity, Created = DateTime.UtcNow });
            else
            {
                existingCartItem.Quantity += quantity;
                existingCartItem.Updated = DateTime.UtcNow;
            }

            await db.SaveChangesAsync();

            var items = await GetCartItems(cart.Id, db)!;
            var vm = GetCartViewModel(items, cache);

            return Results.Extensions.RazorSlice<Slices.Cart, CartVm>(vm!);
        });

        app.MapDelete("/cart/delete/{id}", async (HttpContext http, RazorShopDbContext db, IMemoryCache cache, int id) =>
        {
            var cart = await GetCart(http, db);

            var item = db.CartItems!.Find(id);
            item!.Deleted = true;
            item!.Updated = DateTime.UtcNow;
            await db.SaveChangesAsync();

            var items = await GetCartItems(cart.Id, db)!;
            var shoppingCartVm = GetCartViewModel(items, cache);

            return Results.Extensions.RazorSlice<Slices.CartDelete, CartVm>(shoppingCartVm!);
        });
    }

    private static async Task<Cart> GetCart(HttpContext http, RazorShopDbContext db)
    {
        Cart? cart;

        if (!http.Request.Cookies.TryGetValue("CartSessionId", out var cartSessionGuid))
        {
            var guid = Guid.NewGuid();
            cartSessionGuid = guid.ToString();
            http.Response.Cookies.Append("CartSessionId", cartSessionGuid);

            cart = new Cart { CartGuid = guid, Created = DateTime.UtcNow };
            db.Carts!.Add(cart);
            await db.SaveChangesAsync();
        }
        else
            cart = await db.Carts!.Where(c => c.CartGuid == Guid.Parse(cartSessionGuid!)).FirstOrDefaultAsync();

        return cart!;
    }

    private static async Task<List<CartItem>>? GetCartItems(int cartId, RazorShopDbContext db)
    {
        return await db.CartItems!.Where(c => c.CartId == cartId && !c.Deleted).Include(c => c.Product).ToListAsync();
    }

    private static CartVm? GetCartViewModel(List<CartItem> items, IMemoryCache cache)
    {
        if (items.Count == 0)
            return new CartVm();

        var sizes = (IEnumerable<Size>)cache.Get("sizes")!;

        return new CartVm {
            CartQuantity = items.Sum(c => c.Quantity),
            CartItems = items.Select(item => new CartItemVm {
                Id = item.Id,
                Name = item.Product!.Name,
                Description = item.Product.Description,
                Price = $"{item.Product.Price:#.00} kr",
                Size = sizes.FirstOrDefault(s => s.Id == item.SizeId)?.Name,
                Quantity = item.Quantity
            }).ToList(),
            CartTotal = $"{items.Sum(c => c.Product!.Price * c.Quantity):#.00} kr"
        };
    }
}