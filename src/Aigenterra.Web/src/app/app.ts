import { Component } from '@angular/core';
import { RouterLink, RouterOutlet } from '@angular/router';

@Component({
  imports: [RouterLink, RouterOutlet],
  selector: 'app-root',
  template: `
    <a class="skip-link" [routerLink]="[]" fragment="main">Skip to content</a>
    <header class="shell-header">
      <div class="shell-header-inner">
        <a class="brand" routerLink="/executions" aria-label="Aigenterra Flight Recorder home"
          ><span class="brand-mark" aria-hidden="true">a</span
          ><span>Aigenterra <span class="brand-product">Flight Recorder</span></span></a
        >
        <span class="edition">Developer preview</span>
      </div>
    </header>
    <main id="main" tabindex="-1"><router-outlet /></main>
    <footer class="shell-footer">
      Recorded executions. Inspectable evidence. <span>Local development</span>
    </footer>
  `,
})
export class App {}
