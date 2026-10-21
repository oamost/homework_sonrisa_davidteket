import { Routes } from '@angular/router';
import { AdminComponent } from '../../admin/admin.component';
import { LandingComponent } from './landing.component';
import { SubscriptionsComponent } from './subscriptions/subscriptions.component';

export const appRoutes: Routes = [
  { path: '', component: LandingComponent, pathMatch: 'full' },
  { path: 'subscriptions', component: SubscriptionsComponent },
  { path: 'admin', component: AdminComponent },
];
