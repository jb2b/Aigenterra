import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';
@Component({
  imports: [RouterLink],
  template: `<section class="state panel">
    <h1>Page not found</h1>
    <p>This page does not exist.</p>
    <a routerLink="/executions">Back to executions</a>
  </section>`,
})
export class NotFound {}
