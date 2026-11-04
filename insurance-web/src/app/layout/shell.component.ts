import { Component, ElementRef, HostListener, computed, effect, inject, signal, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { filter } from 'rxjs';
import { BRAND } from '../core/brand';
import { AuthService } from '../core/services/auth.service';
import { initialsOf } from '../core/utils/initials';
import { BrandLogoComponent } from '../shared/components/brand-logo.component';
import { ThemeToggleComponent } from '../shared/components/theme-toggle.component';
import { VerifyEmailBannerComponent } from '../shared/components/verify-email-banner.component';

// Wraps every logged-in page: nav bar on top, the current page in <router-outlet />.
@Component({
  selector: 'app-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, ThemeToggleComponent, BrandLogoComponent, VerifyEmailBannerComponent],
  templateUrl: "shell.html",
  styleUrl: "./shell-component.css",
})
export class ShellComponent {
  auth = inject(AuthService);
  private router = inject(Router);

  readonly name = BRAND.name;
  readonly tagline = BRAND.shortSlogan;
  readonly year = new Date().getFullYear();

  initials = computed(() => initialsOf(this.auth.user()?.fullName));

  menuOpen = signal(false);       // the hamburger panel (narrow screens)
  userMenuOpen = signal(false);   // the account dropdown (wide screens)

  private header = viewChild<ElementRef<HTMLElement>>('header');
  private userMenu = viewChild<ElementRef<HTMLElement>>('userMenu');
  private userButton = viewChild<ElementRef<HTMLButtonElement>>('userButton');

  // Fixed (not random) values so the rain looks the same on every visit.
  // Negative delays mean the rain is already falling when the page loads.
  readonly drops = Array.from({ length: 44 }, (_, i) => ({
    left: (i * 23.7) % 100,
    delay: -(((i * 37) % 19) / 10),
    duration: 1.2 + ((i * 53) % 10) / 10,
    length: 10 + ((i * 29) % 12),
  }));

  constructor() {
    // The stored user can be stale (e.g. verified their email on another device): reload it once per visit.
    this.auth.refreshUser();

    // An admin reset the password while this session was open: go straight to "choose a new password".
    effect(() => {
      if (this.auth.mustChangePassword()) this.router.navigate(['/choose-new-password']);
    });

    // Close any open menu after navigating
    this.router.events
      .pipe(filter((e) => e instanceof NavigationEnd), takeUntilDestroyed())
      .subscribe(() => this.closeMenus());
  }

  toggleMenu(): void {
    this.menuOpen.update((open) => !open);
    this.userMenuOpen.set(false);
  }

  toggleUserMenu(): void {
    this.userMenuOpen.update((open) => !open);
  }

  closeMenus(): void {
    this.menuOpen.set(false);
    this.userMenuOpen.set(false);
  }

  logout(): void {
    this.closeMenus();
    this.auth.logout();
  }

  // Click outside: close the account dropdown, or the hamburger panel
  @HostListener('document:click', ['$event'])
  onDocumentClick(event: MouseEvent): void {
    const target = event.target as Node;
    if (this.userMenuOpen() && !this.userMenu()?.nativeElement.contains(target)) {
      this.userMenuOpen.set(false);
    }
    if (this.menuOpen() && !this.header()?.nativeElement.contains(target)) {
      this.menuOpen.set(false);
    }
  }

  // Escape closes whatever is open, and returns focus to the account button
  @HostListener('document:keydown.escape')
  onEscape(): void {
    const hadUserMenu = this.userMenuOpen();
    this.closeMenus();
    if (hadUserMenu) this.userButton()?.nativeElement.focus();
  }
}
